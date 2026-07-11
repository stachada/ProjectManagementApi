namespace Ordinis.Api.Projects.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/projects/{projectId}/boards</c>.
/// </summary>
/// <param name="CreatedByUserId">The user creating the board. Must exist.</param>
/// <param name="Name">Display name for the new board. Must be unique within the project (case-insensitive).</param>
public sealed record CreateBoardRequest(
    Guid CreatedByUserId,
    string Name);
