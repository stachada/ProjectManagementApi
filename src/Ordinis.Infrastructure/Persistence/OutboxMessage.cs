using System.Text.Json;
using Ordinis.Domain.Common;

namespace Ordinis.Infrastructure.Persistence;

/// <summary>
/// Represents a domain event serialized and stored in the database as part of the Outbox pattern.
/// Rows are written atomically with the aggregate changes that raised the event;
/// a background <c>OutboxDispatcherJob</c> reads unprocessed rows and dispatches them.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>UUIDv7 — time-ordered, client-side generated.</summary>
    public Guid Id { get; private set; }

    /// <summary>UTC timestamp copied from <see cref="IDomainEvent.OccurredAt"/>.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// Assembly-qualified type name of the domain event (e.g. <c>Ordinis.Domain.Tasks.TaskCreated</c>).
    /// Used by the dispatcher to deserialize back to the concrete event type.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>JSON-serialized domain event payload.</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>
    /// Set by <c>OutboxDispatcherJob</c> once the event has been successfully dispatched.
    /// <c>null</c> means the row is pending processing.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    private OutboxMessage() { }

    /// <summary>
    /// Serializes a domain event into an <see cref="OutboxMessage"/> ready for insertion.
    /// </summary>
    public static OutboxMessage From(IDomainEvent domainEvent)
        => new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = domainEvent.OccurredAt,
            Type = domainEvent.GetType().FullName!,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
        };
}
