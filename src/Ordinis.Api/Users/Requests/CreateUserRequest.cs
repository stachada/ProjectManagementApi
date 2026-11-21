using Ordinis.Domain.Users;

namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/users</c>. The new user always joins the authenticated
/// Admin's own organization (the access token's <c>org_id</c> claim) - an Admin cannot create
/// users in another organization.
/// </summary>
/// <param name="DisplayName">Display name for the new user. Must be non-empty.</param>
/// <param name="Email">Email address. Must be unique within the organization.</param>
/// <param name="Password">Plaintext password; hashed before persistence.</param>
/// <param name="OrgRole">Organization-level role. Defaults to <see cref="Role.Member"/>.</param>
public sealed record CreateUserRequest(
    string DisplayName,
    string Email,
    string Password,
    Role OrgRole = Role.Member);
