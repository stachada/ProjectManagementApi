using Ordinis.Domain.Users;

namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/projects/{id}/members</c>.
/// </summary>
/// <param name="UserId">The user to add. Must exist and not already be a member.</param>
/// <param name="Role">The role to assign within the project.</param>
public sealed record AddProjectMemberRequest(Guid UserId, Role Role);
