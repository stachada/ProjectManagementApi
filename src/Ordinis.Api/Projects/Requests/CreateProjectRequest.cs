namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/projects</c>.
/// </summary>
/// <param name="OrganizationId">The owning organization. Must exist and be active.</param>
/// <param name="CreatedByUserId">The user creating the project.</param>
/// <param name="Name">Display name for the new project. Must be non-empty; used to derive a slug unique within the organization.</param>
/// <param name="Description">Optional description of the project's purpose.</param>
public sealed record CreateProjectRequest(
    Guid OrganizationId,
    Guid CreatedByUserId,
    string Name,
    string? Description = null);
