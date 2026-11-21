namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/comments</c>. The author is always the
/// authenticated caller (the access token's <c>sub</c> claim) - never taken from the body.
/// </summary>
/// <param name="Content">Comment text (max 10,000 characters).</param>
public sealed record AddCommentRequest(string Content);
