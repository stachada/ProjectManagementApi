using System.Text.Json;

namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Represents a single audit entry for a project, returned by
/// <c>GET /api/v1/projects/{id}/audit</c>.
/// </summary>
public sealed record AuditEntryDto
{
    /// <summary>
    /// Audit entry identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Type of the audit event.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Timestamp when the audit event occurred.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Identifier of the actor who performed the action.
    /// </summary>
    public required Guid ActorId { get; init; }

    /// <summary>
    /// JSON payload of the audit event, containing details about the action performed.
    /// </summary>
    public required JsonElement Payload { get; init; }
}

/// <summary>
/// Raw Dapper projection of one <c>OutboxMessage</c> row, as selected by
/// <c>GetProjectAuditHandler</c>'s provider-specific SQL. Mapped to <see cref="AuditEntryDto"/>
/// via <c>ProjectMapper.ToAuditEntryDto</c> - kept separate from the DTO because it represents
/// the SQL projection shape, not the API response shape (the DTO's <c>ActorId</c> doesn't exist
/// as a column; it's derived from <see cref="Payload"/> during mapping).
/// </summary>
internal sealed record AuditRow(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    string Payload);
