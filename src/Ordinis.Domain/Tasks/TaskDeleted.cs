using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a <see cref="ProjectTask"/> is soft-deleted.
/// Consumed by the Outbox dispatcher to trigger audit log entries and webhooks.
/// </summary>
public sealed record TaskDeleted(
    Guid TaskId,
    Guid DeletedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
