using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Tasks.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/tasks/{id}</c>.
/// </summary>
/// <param name="Title">New title (max 200 characters).</param>
/// <param name="Description">New description, or <see langword="null"/> to clear it.</param>
/// <param name="Priority">New priority level.</param>
/// <param name="DueDate">New due date, or <see langword="null"/> to clear it.</param>
/// <param name="RequestedByUserId">The user performing the update.</param>
public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    Priority Priority,
    DateTimeOffset? DueDate,
    Guid RequestedByUserId);
