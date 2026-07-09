namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/projects/{id}</c>.
/// </summary>
/// <param name="NewName">New display name. Must not be empty.</param>
/// <param name="NewDescription">New description, or <see langword="null"/> to clear it.</param>
public sealed record UpdateProjectRequest(string NewName, string? NewDescription);
