namespace Ordinis.Api.Organizations.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/organizations/{id}</c>.
/// Renames the organization and replaces its description in a single call.
/// </summary>
/// <param name="Name">The organization's new display name.</param>
/// <param name="Description">The organization's new description, or <see langword="null"/> to clear it.</param>
public sealed record UpdateOrganizationRequest(string Name, string? Description);
