namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/tasks/{id}/comments/{commentId}</c>.
/// </summary>
/// <param name="NewContent">Replacement text (max 10,000 characters).</param>
/// <param name="RequestedByUserId">Must match the comment's original author.</param>
public sealed record EditCommentRequest(string NewContent, Guid RequestedByUserId);
