using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a <see cref="ProjectTask"/>'s scalar details (<c>Title</c>,
/// <c>Description</c>, <c>Priority</c>, <c>DueDate</c>) are updated.
/// </summary>
/// <remarks>
/// <see cref="Changes"/> contains one entry per field whose value actually changed,
/// keyed by property name (e.g. <c>"Priority"</c>), with the before/after value boxed
/// into the tuple - fields the caller resubmitted unchanged are omitted entirely, so
/// audit log entries and webhook payloads only describe what was actually edited.
/// </remarks>
public sealed record TaskUpdated(
    Guid TaskId,
    IReadOnlyDictionary<string, (object? Before, object? After)> Changes,
    Guid UpdatedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
