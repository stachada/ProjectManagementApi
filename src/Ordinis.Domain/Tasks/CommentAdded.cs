using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a <see cref="Comment"/> is added to a <see cref="ProjectTask"/>.
/// Consumed by webhooks and notification logic.
/// </summary>
public sealed record CommentAdded(
    Guid TaskId,
    Guid CommentId,
    Guid AuthorId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
