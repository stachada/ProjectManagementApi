using Ordinis.Domain.Users;

namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/users/{id}/org-role</c>.
/// </summary>
/// <param name="NewOrgRole">The organization-level role to assign.</param>
/// <param name="RequestedByUserId">The ID of the user performing the change.</param>
public sealed record ChangeUserOrgRoleRequest(
    Role NewOrgRole,
    Guid RequestedByUserId);
