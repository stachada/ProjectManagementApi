using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// Background service that polls for due <see cref="WebhookDelivery"/> rows and performs the
/// actual HTTP POST delivery, with exponential-backoff retry.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate <see cref="BackgroundService"/> from <c>OutboxDispatcherJob"</c>,
/// not another <c>IDomainEventHandler{T}</c> — see <c>WebhookDomainEventHandler</c>'s remarks.
/// HTTP calls and backoff waits happen here, entirely outside any DB transaction, so a slow or
/// dead endpoint never holds a row lock.
/// </para>
/// <para>
/// <b>Multi-instance safety:</b> uses the same provider-aware raw-SQL row-locking fetch as
/// <c>OutboxDispatcherJob</c> (<c>UPDLOCK, READPAST</c> / <c>FOR UPDATE SKIP LOCKED</c>, held for
/// the lifetime of an explicit transaction) so two instances never deliver the same row twice.
/// </para>
/// <para>
/// <b>SQL coupling:</b> the raw SQL strings in <see cref="FetchBatchAsync"/> reference table and
/// column names that must stay in sync with <c>WebhookDeliveryConfiguration</c>.
/// </para>
/// </remarks>
internal sealed class WebhookDeliveryDispatcherService : BackgroundService
{
    private const int MaxErrorLength = 2000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebhookUrlGuard _urlGuard;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookDeliveryDispatcherService> _logger;
    private readonly string _databaseProvider;
    private readonly WebhookDeliveryOptions _options;

    public WebhookDeliveryDispatcherService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IWebhookUrlGuard urlGuard,
        TimeProvider timeProvider,
        ILogger<WebhookDeliveryDispatcherService> logger,
        IOptions<OutboxOptions> outboxOptions,
        IOptions<WebhookDeliveryOptions> options)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _urlGuard = urlGuard;
        _timeProvider = timeProvider;
        _logger = logger;
        _databaseProvider = outboxOptions.Value.DatabaseProvider;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "WebhookDeliveryDispatcherService: batch processing failed; will retry on next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during graceful shutdown — mirrors OutboxDispatcherJob's handling of
            // WaitForNextTickAsync's cancellation behavior.
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Same rationale as OutboxDispatcherJob: hold the row locks acquired by FetchBatchAsync
        // for the whole fetch-deliver-save cycle so a second instance can't claim the same batch.
        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        List<WebhookDelivery> deliveries = await FetchBatchAsync(db, now, stoppingToken);

        if (deliveries.Count == 0)
        {
            return;
        }

        HttpClient client = _httpClientFactory.CreateClient("WebhookDelivery");

        foreach (WebhookDelivery delivery in deliveries)
        {
            await DeliverAsync(client, delivery, stoppingToken);
        }

        await db.SaveChangesAsync(stoppingToken);
        await tx.CommitAsync(stoppingToken);
    }

    private async Task<List<WebhookDelivery>> FetchBatchAsync(
        AppDbContext db,
        DateTimeOffset now,
        CancellationToken stoppingToken)
    {
        return _databaseProvider switch
        {
            "SqlServer" => await db.WebhookDeliveries
                .FromSqlInterpolated($"""
                    SELECT TOP ({_options.BatchSize})
                        [Id], [WebhookId], [Url], [Payload], [Status], [AttemptCount], [NextAttemptAt], [LastError], [DeliveredAt]
                    FROM [WebhookDeliveries] WITH (UPDLOCK, READPAST)
                    WHERE [Status] = 'Pending' AND [NextAttemptAt] <= {now}
                    ORDER BY [NextAttemptAt]
                    """)
                .ToListAsync(stoppingToken),

            "PostgreSQL" => await db.WebhookDeliveries
                .FromSqlInterpolated($"""
                    SELECT "Id", "WebhookId", "Url", "Payload", "Status", "AttemptCount", "NextAttemptAt", "LastError", "DeliveredAt"
                    FROM "WebhookDeliveries"
                    WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now}
                    ORDER BY "NextAttemptAt"
                    LIMIT {_options.BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(stoppingToken),

            _ => throw new InvalidOperationException($"Unsupported database provider: {_databaseProvider}")
        };
    }

    private async Task DeliverAsync(HttpClient client, WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Re-checked on every attempt, not just once at registration: this is what defeats DNS
        // rebinding, where a hostname resolves to a public address when first registered but a
        // private one by the time a (re)delivery attempt actually runs.
        if (!await _urlGuard.IsAllowedAsync(delivery.Url, cancellationToken))
        {
            RecordFailure(delivery, now, "Blocked: URL resolves to a private, loopback, or otherwise disallowed network address.");
            return;
        }

        try
        {
            using var content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.PostAsync(delivery.Url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = WebhookDeliveryStatus.Delivered;
                delivery.DeliveredAt = now;
                delivery.LastError = null;
                return;
            }

            RecordFailure(delivery, now, $"Received HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own request timeout (WebhookDeliveryOptions.HttpTimeout) throws
            // OperationCanceledException even though the host's stoppingToken was never
            // triggered — checking IsCancellationRequested (not the exception type) is what
            // correctly distinguishes "this delivery timed out" (a retryable failure) from
            // "the host is shutting down" (let it propagate, don't record a bogus failure).
            string message = ex is OperationCanceledException ? "Request timed out." : ex.Message;
            RecordFailure(delivery, now, message);
        }
    }

    private void RecordFailure(WebhookDelivery delivery, DateTimeOffset now, string error)
    {
        delivery.AttemptCount++;
        delivery.LastError = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];

        if (delivery.AttemptCount >= _options.MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            _logger.LogError(
                "WebhookDeliveryDispatcherService: delivery {DeliveryId} to {Url} failed after {AttemptCount} attempts; marking failed. Last error: {LastError}",
                delivery.Id, delivery.Url, delivery.AttemptCount, delivery.LastError);
        }
        else
        {
            TimeSpan backoff = _options.InitialBackoff * Math.Pow(_options.BackoffMultiplier, delivery.AttemptCount - 1);
            delivery.NextAttemptAt = now + backoff;
            _logger.LogWarning(
                "WebhookDeliveryDispatcherService: delivery {DeliveryId} to {Url} failed (attempt {AttemptCount}/{MaxAttempts}); will retry at {NextAttemptAt}. Last error: {LastError}",
                delivery.Id, delivery.Url, delivery.AttemptCount, _options.MaxAttempts, delivery.NextAttemptAt, delivery.LastError);
        }
    }
}
