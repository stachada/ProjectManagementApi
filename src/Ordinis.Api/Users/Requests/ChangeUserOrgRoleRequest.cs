using Ordinis.Domain.Users;

namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/users/{id}/org-role</c>. The user performing the change is
/// always the authenticated caller.
/// </summary>
/// <param name="NewOrgRole">The organization-level role to assign.</param>
public sealed record ChangeUserOrgRoleRequest(Role NewOrgRole);
