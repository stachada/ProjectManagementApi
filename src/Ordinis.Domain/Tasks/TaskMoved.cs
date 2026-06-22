using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a <see cref="ProjectTask"/> transitions to a new status.
/// Carries both the previous and new status to allow consumers to react
/// to specific transitions (e.g. notify the reporter when a task moves to Done).
/// </summary>
public sealed record TaskMoved(
    Guid TaskId,
    ProjectTaskStatus PreviousStatus,
    ProjectTaskStatus NewStatus,
    Guid MovedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
