using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordinis.Application.Common;
using Ordinis.Domain.Common;

namespace Ordinis.Infrastructure.Persistence;

/// <summary>
/// Background service that polls the Outbox for unprocessed domain events,
/// dispatches each to its registered <see cref="IDomainEventHandler{TEvent}"/> implementations,
/// and marks the row processed.
/// </summary>
/// <remarks>
/// <para>
/// Delivery guarantee: at-least-once per attempt. A message is retried up to
/// <c>MaxRetries</c> times before being marked dead. Handlers must be idempotent.
/// </para>
/// <para>
/// <b>Multi-instance safety:</b> <see cref="ProcessBatchAsync"/> wraps the entire
/// fetch-dispatch-save cycle in an explicit transaction. <c>FOR UPDATE SKIP LOCKED</c>
/// (PostgreSQL) and <c>WITH (UPDLOCK, READPAST)</c> (SQL Server) only hold row locks
/// for the lifetime of the transaction that issued the SELECT — without
/// <c>BeginTransactionAsync</c>, both hints are ineffective because the autocommit
/// transaction releases the locks the moment the SELECT completes, before any handler
/// or <c>SaveChangesAsync</c> runs.
/// </para>
/// <para>
/// <b>SQL coupling:</b> the raw SQL strings in <see cref="FetchBatchAsync"/> reference
/// table and column names that must stay in sync with <c>OutboxMessageConfiguration</c>.
/// If the table or column names change in the EF Core configuration, update the queries here.
/// </para>
/// </remarks>
internal sealed class OutboxDispatcherJob : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;
    private const int MaxRetries = 3;
    private const int MaxErrorLength = 2000;

    // Paid once per closed handler type — eliminates repeated GetMethod reflection on the hot path.
    private static readonly ConcurrentDictionary<Type, MethodInfo> MethodCache = new();

    // Paid once per CLR type name — avoids rescanning all loaded assemblies on every dispatch.
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxDispatcherJob> _logger;
    private readonly string _databaseProvider;

    public OutboxDispatcherJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcherJob> logger,
        IOptions<OutboxOptions> options)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _databaseProvider = options.Value.DatabaseProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and continue — a transient DB error must not stop the host.
                _logger.LogError(ex, "OutboxDispatcherJob: batch processing failed; will retry on next tick.");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // FIX: wrap the entire fetch-dispatch-save cycle in an explicit transaction so the
        // row-level locks acquired by FetchBatchAsync (FOR UPDATE SKIP LOCKED / UPDLOCK) are
        // held until SaveChangesAsync commits. Without BeginTransactionAsync, both locking
        // hints run in an autocommit transaction that ends immediately after the SELECT,
        // releasing all locks before DispatchAsync or SaveChangesAsync run — a second replica
        // can claim the same batch in that window, causing duplicate event dispatch.
        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

        List<OutboxMessage> messages = await FetchBatchAsync(db, stoppingToken);

        if (messages.Count == 0)
        {
            // Nothing to process; tx is disposed without CommitAsync → autorollback (no-op).
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (OutboxMessage message in messages)
        {
            await DispatchAsync(scope.ServiceProvider, message, now, stoppingToken);
        }

        await db.SaveChangesAsync(stoppingToken);
        await tx.CommitAsync(stoppingToken);
    }

    private async Task<List<OutboxMessage>> FetchBatchAsync(AppDbContext db, CancellationToken stoppingToken)
    {
        // FIX: use explicit column names instead of SELECT * to make the coupling to
        // OutboxMessageConfiguration visible. If OutboxMessageConfiguration changes the table
        // name or a column name, this query must also be updated (the class-level remarks doc
        // this contract). Column names must match OutboxMessageConfiguration exactly.
        return _databaseProvider switch
        {
            // WITH (UPDLOCK, READPAST): UPDLOCK promotes to update-intent lock;
            // READPAST skips already-locked rows so other replicas do not block waiting.
            // TOP is not parameterizable in T-SQL without parentheses; wrapping in parentheses
            // makes it parameterizable — FromSqlInterpolated sends BatchSize as @p0,
            // producing SELECT TOP (@p0) which is valid T-SQL since SQL Server 2005.
            "SqlServer" => await db.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT TOP ({BatchSize})
                        [Id], [OccurredAt], [Type], [Payload], [ProcessedAt], [RetryCount], [Error]
                    FROM [OutboxMessages] WITH (UPDLOCK, READPAST)
                    WHERE [ProcessedAt] IS NULL
                    ORDER BY [OccurredAt]
                    """)
                .ToListAsync(stoppingToken),

            // FOR UPDATE SKIP LOCKED: other replicas skip already-locked rows instead of blocking.
            // LIMIT is parameterizable in PostgreSQL; FromSqlInterpolated sends BatchSize as @p0.
            "PostgreSQL" => await db.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT "Id", "OccurredAt", "Type", "Payload", "ProcessedAt", "RetryCount", "Error"
                    FROM "OutboxMessages"
                    WHERE "ProcessedAt" IS NULL
                    ORDER BY "OccurredAt"
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(stoppingToken),

            _ => throw new InvalidOperationException($"Unsupported database provider: {_databaseProvider}")
        };
    }

    private async Task DispatchAsync(
        IServiceProvider services,
        OutboxMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            Type? eventType = ResolveEventType(message.Type);
            if (eventType is null)
            {
                _logger.LogError(
                    "OutboxDispatcherJob: could not resolve CLR type {EventType} for message {MessageId}; marking dead.",
                    message.Type, message.Id);
                message.ProcessedAt = now;
                return;
            }

            var domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(message.Payload, eventType);
            if (domainEvent is null)
            {
                _logger.LogError(
                    "OutboxDispatcherJob: deserialization returned null for message {MessageId} of type {EventType}; marking dead.",
                    message.Id, message.Type);
                message.ProcessedAt = now;
                return;
            }

            await InvokeHandlersAsync(services, eventType, domainEvent, cancellationToken);
            message.ProcessedAt = now;
        }
        catch (Exception ex)
        {
            message.RetryCount++;
            message.Error = ex.Message.Length <= MaxErrorLength
                ? ex.Message
                : ex.Message[..MaxErrorLength];

            if (message.RetryCount >= MaxRetries)
            {
                _logger.LogError(
                    ex,
                    "OutboxDispatcherJob: message {MessageId} of type {EventType} failed after {RetryCount} attempts; marking dead.",
                    message.Id, message.Type, message.RetryCount);
                message.ProcessedAt = now;
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "OutboxDispatcherJob: message {MessageId} of type {EventType} failed (attempt {RetryCount}/{MaxRetries}); will retry.",
                    message.Id, message.Type, message.RetryCount, MaxRetries);
            }
        }
    }

    private static async Task InvokeHandlersAsync(
        IServiceProvider services,
        Type eventType,
        IDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        Type handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        IEnumerable<object?> handlers = services.GetServices(handlerType);
        MethodInfo method = MethodCache.GetOrAdd(handlerType, static t => t.GetMethod("HandleAsync")!);

        foreach (object? handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw; // unreachable — satisfies the compiler
            }
        }
    }

    private static Type? ResolveEventType(string typeName) =>
        TypeCache.GetOrAdd(
            typeName,
            static name => AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(name))
                .FirstOrDefault(t => t is not null));
}
