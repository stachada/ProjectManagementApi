namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/assign</c>.
/// </summary>
/// <param name="AssigneeId">The user to assign the task to.</param>
/// <param name="RequestedByUserId">The user performing the assignment.</param>
public sealed record AssignTaskRequest(
    Guid AssigneeId,
    Guid RequestedByUserId);
