using Ordinis.Domain.Common;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Raised when a new <see cref="ProjectTask"/> is created.
/// Consumed by the Outbox dispatcher to trigger audit log entries and webhooks.
/// </summary>
public sealed record TaskCreated(
    Guid TaskId,
    Guid BoardId,
    Guid ReporterId,
    string Title,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc/>
    public Guid EventId { get; } = Guid.CreateVersion7();
}
