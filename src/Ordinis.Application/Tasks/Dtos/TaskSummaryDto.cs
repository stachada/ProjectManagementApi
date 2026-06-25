using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Dtos;

/// <summary>
/// Lean projection of a task used in paginated list responses
/// (<c>GET /api/v1/tasks</c>, <c>GET /api/v1/boards/{id}/tasks</c>. etc.).
/// Does not include nested collections - use <see cref="TaskDto"/> for full detail.
/// </summary>
public class TaskSummaryDto
{
    /// <summary>
    /// Unique identifier of the task.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// ID of the board this task belongs to.
    /// </summary>
    public Guid BoardId { get; init; }

    /// <summary>
    /// Short title of the task.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Current workflow status.
    /// </summary>
    public ProjectTaskStatus Status { get; init; }

    /// <summary>
    /// Current priority level.
    /// </summary>
    public Priority Priority { get; init; }

    /// <summary>
    /// User ID of the current assigned user, or <see langword="null"/> if unassigned.
    /// </summary>
    public Guid? AssigneeId { get; init; }

    /// <summary>
    /// Display name of the assignee, or <see cref="null"/> if unassigned.
    /// Resolved from the User aggregate at query time.
    /// </summary>
    public string? AssigneeDisplayName { get; init; }

    /// <summary>
    /// Optional due date for this task.
    /// </summary>
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>
    /// UTC timestamp when this task was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp of the most recent update to this task.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Number of non-deleted comments on this task.
    /// </summary>
    public int CommentCount { get; init; }

    /// <summary>
    /// Number of attachments on this task.
    /// </summary>
    public int AttachmentCount { get; init; }
}
