namespace Ordinis.Domain.Common;

/// <summary>
/// Marker interface for all domain events raised by aggregate roots.
/// Domain events represent something that happened in the domain - past tense,
/// immutable facts. They are dispatched after the
/// transaction commits via the Outbox pattern.
/// </summary>
/// <remarks>
/// <example>
/// Implement this interface on a sealed record for each event.
/// Records are preferred over classes because they are immutable
/// by default and provide structural equality for free.
/// <code>
/// public sealed record TaskCreated(Guid TaskId, Guid ProjectId, string Title) : IDomainEvent;
/// </code>
/// </example>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>
    /// Unique identifier for this specific event occurrence.
    /// Used by the Outbox pattern to guarantee exactly-once delivery.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// UTC timestamp of when the event was raised.
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
