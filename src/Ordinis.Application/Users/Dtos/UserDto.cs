namespace Ordinis.Application.Users.Dtos;

/// <summary>
/// Full detail view of a user account.
/// Returned by <c>GetUserById</c>.
/// </summary>
/// <remarks>
/// Auth-sensitive fields (<c>PasswordHash</c>, <c>RefreshToken</c>,
/// <c>RefreshTokenExpiresAt</c>) are never included here.
/// </remarks>
public sealed record UserDto
{
    /// <summary>
    /// The user's unique identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The user's display name shown accross the UI.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The user's email address (login identifier).
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// The user's organization-level role.
    /// </summary>
    public required string OrgRole { get; init; }

    /// <summary>
    /// Whether the user account is currently active.
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// The ID of the organization this user belongs to.
    /// </summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// The name of the organization this user belongs to.
    /// Resolved by the query handler via a scalar lookup - avoids a
    /// navigation property crossing the Organization aggregate boundary.
    /// </summary>
    public required string OrganizationName { get; init; }

    /// <summary>
    /// When the user account was created (UTC).
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the user account was last updated (UTC).
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
