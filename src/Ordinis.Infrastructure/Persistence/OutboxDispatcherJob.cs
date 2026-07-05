using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// <b>Multi-instance note:</b> the SELECT query does not use row-level locking.
/// In a multi-replica deployment, replace the LINQ query with a provider-specific
/// <c>FOR UPDATE SKIP LOCKED</c> (PostgreSQL) or <c>WITH (UPDLOCK, READPAST)</c>
/// (SQL Server) query via <c>FromSqlInterpolated</c>, wired in
/// <c>InfrastructureServiceExtensions</c> once the active provider is known.
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

    public OutboxDispatcherJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcherJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
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

        List<OutboxMessage> messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        if (messages.Count == 0)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (OutboxMessage message in messages)
        {
            await DispatchAsync(scope.ServiceProvider, message, now, stoppingToken);
        }

        await db.SaveChangesAsync(stoppingToken);
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
