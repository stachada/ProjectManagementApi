namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/unassign</c>.
/// </summary>
/// <param name="RequestedByUserId">The user performing the unassignment.</param>
public sealed record UnassignTaskRequest(Guid RequestedByUserId);
