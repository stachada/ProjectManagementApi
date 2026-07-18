namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/reopen</c>.
/// </summary>
/// <param name="RequestedByUserId">The user performing the reopen.</param>
public sealed record ReopenTaskRequest(Guid RequestedByUserId);
