namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Lean project view returned by paginated list queries
/// (e.g <c>GET /api/v1/projects</c>, <c>GET /api/v1/organizations/{id}/projects</c>).
/// Does not include nested board or member collections - use
/// <see cref="ProjectDto"/> for the full detail view.
/// </summary>
public sealed record ProjectSummaryDto
{
    /// <summary>
    /// Unique project identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Display name of the project.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-friendly unique identifier scoped to the organization.
    /// Immutable after creation.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Optional description of the project's purpose and scope.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the project is archived (read-only).
    /// </summary>
    public required bool IsArchived { get; init; }

    /// <summary>
    /// The ID of the organization this project belongs to.
    /// </summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// The ID of the user who created the project.
    /// </summary>
    public required Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Total number of members in the project.
    /// </summary>
    public required int MemberCount { get; init; }

    /// <summary>
    /// Total number of boards in the project.
    /// </summary>
    public required int BoardCount { get; init; }

    /// <summary>
    /// UTC timestamp when the project was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
