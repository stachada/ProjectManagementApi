namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/close</c>.
/// </summary>
/// <param name="RequestedByUserId">The user performing the close.</param>
public sealed record CloseTaskRequest(Guid RequestedByUserId);
