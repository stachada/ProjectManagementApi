using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// Subscribes to the domain events webhooks can fire on and enqueues a
/// <see cref="WebhookDelivery"/> row for every matching registered webhook.
/// </summary>
/// <remarks>
/// <para>
/// Registered as an <see cref="IDomainEventHandler{TEvent}"/> implementation, invoked by
/// <c>OutboxDispatcherJob</c> from within its own fetch-dispatch-save transaction. This handler
/// deliberately does no I/O beyond the same <see cref="AppDbContext"/> — it only inserts cheap
/// tracking rows — so it stays fast and does not hold DB locks open for HTTP calls. The actual
/// HTTP delivery (with retry/backoff) happens later, out of process, in
/// <see cref="WebhookDeliveryDispatcherService"/>.
/// </para>
/// <para>
/// Domain events carry a <c>TaskId</c> but not a <c>ProjectId</c>; each handler method resolves
/// the owning project via <c>Task -&gt; Board -&gt; Project</c> before looking up registered webhooks.
/// </para>
/// </remarks>
/// <param name="db"></param>
public sealed class WebhookDomainEventHandler(AppDbContext db) :
    IDomainEventHandler<TaskCreated>,
    IDomainEventHandler<TaskMoved>,
    IDomainEventHandler<TaskAssigned>,
    IDomainEventHandler<CommentAdded>
{
    /// <inheritdoc/>
    public Task HandleAsync(TaskCreated domainEvent, CancellationToken cancellationToken = default)
        => EnqueueAsync(domainEvent.TaskId, WebhookEventTypes.TaskCreated, domainEvent, domainEvent.OccurredAt, cancellationToken);

    /// <inheritdoc/>
    public Task HandleAsync(TaskMoved domainEvent, CancellationToken cancellationToken = default)
        => EnqueueAsync(domainEvent.TaskId, WebhookEventTypes.TaskMoved, domainEvent, domainEvent.OccurredAt, cancellationToken);

    /// <inheritdoc/>
    public Task HandleAsync(TaskAssigned domainEvent, CancellationToken cancellationToken = default)
        => EnqueueAsync(domainEvent.TaskId, WebhookEventTypes.TaskAssigned, domainEvent, domainEvent.OccurredAt, cancellationToken);

    /// <inheritdoc/>
    public Task HandleAsync(CommentAdded domainEvent, CancellationToken cancellationToken = default)
        => EnqueueAsync(domainEvent.TaskId, WebhookEventTypes.CommentAdded, domainEvent, domainEvent.OccurredAt, cancellationToken);

    private async Task EnqueueAsync(
        Guid taskId,
        string eventName,
        object data,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        // Cast to (Guid?) — SingleOrDefaultAsync over a non-nullable Guid projection returns
        // Guid.Empty (not null) when the task doesn't exist, which would otherwise silently
        // resolve to a bogus "project" and never match any webhook.
        Guid? projectId = await db.Tasks
            .Where(t => t.Id == taskId)
            .Select(t => (Guid?)t.Board!.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        if (projectId is null)
        {
            return;
        }

        List<Webhook> webhooks = await db.Webhooks
            .Where(w => w.ProjectId == projectId.Value)
            .ToListAsync(cancellationToken);

        if (webhooks.Count == 0)
        {
            return;
        }

        // Serialize via the concrete runtime type (matches OutboxMessage.From's approach) so the
        // embedded "data" element reflects the domain event's own fields, not a generic `object`.
        JsonElement dataElement = JsonSerializer.SerializeToElement(data, data.GetType());
        var envelope = new WebhookEnvelope(eventName, occurredAt, dataElement);
        string payload = JsonSerializer.Serialize(envelope);

        foreach (Webhook webhook in webhooks)
        {
            if (webhook.EventTypes.Count > 0 && !webhook.EventTypes.Contains(eventName))
            {
                continue;
            }

            db.WebhookDeliveries.Add(WebhookDelivery.Create(webhook.Id, webhook.Url, payload, occurredAt));
        }
    }
}

/// <summary>
/// The JSON envelope delivered to every subscribed webhook URL:
/// <c>{ "event": "task.moved", "occurredAt": "...", "data": { ... } }</c>.
/// </summary>
internal sealed record WebhookEnvelope(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("data")] JsonElement Data);
