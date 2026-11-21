namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/tasks/{id}/comments/{commentId}</c>. The authenticated
/// caller must be the comment's original author.
/// </summary>
/// <param name="NewContent">Replacement text (max 10,000 characters).</param>
public sealed record EditCommentRequest(string NewContent);
