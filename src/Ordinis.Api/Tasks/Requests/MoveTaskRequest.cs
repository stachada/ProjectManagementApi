using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/move</c>. The user performing the move is
/// always the authenticated caller.
/// </summary>
/// <param name="Status">Target status. Must be a legal transition from the task's current status.</param>
public sealed record MoveTaskRequest(ProjectTaskStatus Status);
