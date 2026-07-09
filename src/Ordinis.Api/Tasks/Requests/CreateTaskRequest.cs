using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/tasks</c>.
/// </summary>
/// <param name="BoardId">The board to create the task on. Must exist and not be archived.</param>
/// <param name="Title">Short title for the task (max 200 characters).</param>
/// <param name="Description">Optional long-form description.</param>
/// <param name="Priority">Initial priority.</param>
/// <param name="AssigneeId">Optional user to immediately assign the task to.</param>
/// <param name="DueDate">Optional due date.</param>
/// <param name="RequestedByUserId">The user creating the task.</param>
public sealed record CreateTaskRequest(
    Guid BoardId,
    string Title,
    string? Description,
    Priority Priority,
    Guid? AssigneeId,
    DateTimeOffset? DueDate,
    Guid RequestedByUserId);
