namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/projects/{projectId}/boards</c>. The creator is always the
/// authenticated caller.
/// </summary>
/// <param name="Name">Display name for the new board. Must be unique within the project (case-insensitive).</param>
public sealed record CreateBoardRequest(string Name);
