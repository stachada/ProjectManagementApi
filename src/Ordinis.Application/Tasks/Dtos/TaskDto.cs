using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Dtos;

/// <summary>
/// Full detail projection of a task, including nested comments and attachments.
/// Returned by <c>GET /api/v1/tasks/{id}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConcurrencyToken"/> is a Base64-encoded representation of the task's
/// <c>RowVersion</c> byte array. The API layer returns it as an <c>ETag</c> response header.
/// Clients must echo it back via <c>If-Match</c> on <c>PUT</c> and state-transition requests.
/// </para>
/// </remarks>
public class TaskDto
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
    /// Short title of the task (max 200 characters).
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Optional long-form description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Current workflow status.
    /// </summary>
    public ProjectTaskStatus Status { get; init; }

    /// <summary>
    /// Current priority level.
    /// </summary>
    public Priority Priority { get; init; }

    /// <summary>
    /// User ID of the currently assigned user, or <see langword="null"/> if unassigned.
    /// </summary>
    public Guid? AssigneeId { get; init; }

    /// <summary>
    /// Display name of the assignees, or <see langword="null"/> if unassigned.
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
    /// Base64-encoded <c>RowVersion</c> used for optimistic concurrency.
    /// Returned as the <c>ETag</c> response header by the API layer.
    /// Clients echo it back via <c>If-Match</c> on mutating requests.
    /// </summary>
    public string ConcurrencyToken { get; init; } = string.Empty;

    /// <summary>
    /// All non-deleted comments on this task, ordered by creation time ascending.
    /// </summary>
    public IReadOnlyList<CommentDto> Comments { get; init; } = [];

    /// <summary>
    /// All attachments on this task, ordered by upload time ascending.
    /// </summary>
    public IReadOnlyList<AttachmentDto> Attachments { get; init; } = [];
}
