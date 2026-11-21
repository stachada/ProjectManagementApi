namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/projects</c>. The creator is always the authenticated
/// caller, who must belong to <paramref name="OrganizationId"/> and becomes the project's first
/// Admin member.
/// </summary>
/// <param name="OrganizationId">The owning organization. Must exist, be active, and be the caller's own organization.</param>
/// <param name="Name">Display name for the new project. Must be non-empty; used to derive a slug unique within the organization.</param>
/// <param name="Description">Optional description of the project's purpose.</param>
public sealed record CreateProjectRequest(
    Guid OrganizationId,
    string Name,
    string? Description = null);
