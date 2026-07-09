using Ordinis.Domain.Users;

namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/projects/{id}/members/{userId}/role</c>.
/// </summary>
/// <param name="NewRole">The new role to assign to the member.</param>
public sealed record ChangeMemberRoleRequest(Role NewRole);
