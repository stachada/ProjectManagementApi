namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/boards/{id}/name</c>.
/// </summary>
/// <param name="NewName">The new name. Must be unique within the project (case-insensitive).</param>
public sealed record RenameBoardRequest(string NewName);
