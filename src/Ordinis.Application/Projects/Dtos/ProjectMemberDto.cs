using Ordinis.Domain.Users;

namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Represents a single project membership, embedded in <see cref="ProjectDto"/>
/// and returned by <c>GET /api/v1/projects/{id}/members</c>.
/// </summary>
public sealed record ProjectMemberDto
{
    /// <summary>
    /// Membership record identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The ID of the member user.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// The display name of the member user.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The member's role within this project.
    /// </summary>
    public required Role Role { get; init; }

    /// <summary>
    /// UTC timestamp when the user joined the project.
    /// </summary>
    public required DateTimeOffset JoinedAt { get; init; }
}
