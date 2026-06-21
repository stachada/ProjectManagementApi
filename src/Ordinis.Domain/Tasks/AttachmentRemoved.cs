using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a file attachment is removed from a <see cref="ProjectTask"/>.
/// The Application layer is responsible for deleting the file from blob
/// storage after this event is processed.
/// </summary>
public sealed record AttachmentRemoved(
    Guid TaskId,
    Guid AttachmentId,
    Guid RemovedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
