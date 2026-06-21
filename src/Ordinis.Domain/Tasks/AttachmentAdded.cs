using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a file attachment is added to a <see cref="ProjectTask"/>.
/// Consumed by the Outbox dispatcher to trigger audit log entries.
/// </summary>
public sealed record AttachmentAdded(
    Guid TaskId,
    Guid AttachmentId,
    string FileName,
    Guid UploadedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
