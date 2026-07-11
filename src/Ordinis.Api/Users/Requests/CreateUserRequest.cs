using Ordinis.Domain.Users;

namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/users</c>.
/// </summary>
/// <param name="OrganizationId">The owning organization. Must exist and be active.</param>
/// <param name="DisplayName">Display name for the new user. Must be non-empty.</param>
/// <param name="Email">Email address. Must be unique within the organization.</param>
/// <param name="Password">Plaintext password; hashed before persistence.</param>
/// <param name="OrgRole">Organization-level role. Defaults to <see cref="Role.Member"/>.</param>
public sealed record CreateUserRequest(
    Guid OrganizationId,
    string DisplayName,
    string Email,
    string Password,
    Role OrgRole = Role.Member);
