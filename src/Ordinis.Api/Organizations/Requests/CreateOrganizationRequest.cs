namespace Ordinis.Api.Organizations.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/organizations</c>.
/// </summary>
/// <param name="Name">Display name for the new organization. Must be non-empty; used to derive a globally unique slug.</param>
/// <param name="Description">Optional description of the organization's purpose.</param>
public sealed record CreateOrganizationRequest(string Name, string? Description = null);
