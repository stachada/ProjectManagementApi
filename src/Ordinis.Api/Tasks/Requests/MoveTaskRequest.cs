using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks/{id}/move</c>.
/// </summary>
/// <param name="Status">Target status. Must be a legal transition from the task's current status.</param>
/// <param name="RequestedByUserId">The user performing the move.</param>
public sealed record MoveTaskRequest(
    ProjectTaskStatus Status,
    Guid RequestedByUserId);
