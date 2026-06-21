using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a <see cref="Comment"/> is soft-deleted from a <see cref="ProjectTask"/>.
/// Consumed by the Outbox dispatcher to trigger audit log entries.
/// </summary>
public sealed record CommentRemoved(
    Guid TaskId,
    Guid CommentId,
    Guid RemovedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
