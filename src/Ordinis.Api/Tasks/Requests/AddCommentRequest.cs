namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/comments</c>.
/// </summary>
/// <param name="AuthorId">The user posting the comment.</param>
/// <param name="Content">Comment text (max 10,000 characters).</param>
public sealed record AddCommentRequest(Guid AuthorId, string Content);
