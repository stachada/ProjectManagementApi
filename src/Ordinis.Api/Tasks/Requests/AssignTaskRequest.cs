namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/assign</c>. The user performing the
/// assignment is always the authenticated caller.
/// </summary>
/// <param name="AssigneeId">The user to assign the task to.</param>
public sealed record AssignTaskRequest(Guid AssigneeId);
