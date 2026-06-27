namespace Ordinis.Application.Organizations.Dtos;

/// <summary>
/// Full detail view of an organization.
/// Returned by <c>GetOrganizationById</c>.
/// </summary>
public sealed record OrganizationDto
{
    /// <summary>
    /// The organization's unique identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The organization's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description of the organization's purpose.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether the organization is currently active.
    /// Suspended organizations reject new projects and user invitations.
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// UTC timestamp when the organization was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Total number of projects belonging to this organization.
    /// </summary>
    public required int ProjectCount { get; init; }
}
