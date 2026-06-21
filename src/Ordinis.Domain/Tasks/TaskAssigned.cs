using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a <see cref="ProjectTask"/> is assigned to a user.
/// Consumed by webhooks and notification logic to alert the new assignee.
/// </summary>
public sealed record TaskAssigned(
    Guid TaskId,
    Guid AssigneeId,
    Guid AssignedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
