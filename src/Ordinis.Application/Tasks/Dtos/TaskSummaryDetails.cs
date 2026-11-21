namespace Ordinis.Application.Tasks.Dtos;

/// <summary>
/// Per-task values a <see cref="TaskSummaryDto"/> needs that don't live on the
/// <see cref="Domain.Tasks.ProjectTask"/> row itself, resolved by the list handler in one
/// batched query and passed to <see cref="TaskMapper.ToSummaryDto"/>.
/// </summary>
/// <param name="AssigneeDisplayName">The assignee's display name, or <see langword="null"/> if unassigned.</param>
/// <param name="CommentCount">Number of non-deleted comments on the task.</param>
/// <param name="AttachmentCount">Number of attachments on the task.</param>
public sealed record TaskSummaryDetails(string? AssigneeDisplayName, int CommentCount, int AttachmentCount)
{
    /// <summary>
    /// Details for a task with no assignee, comments, or attachments.
    /// </summary>
    public static TaskSummaryDetails None { get; } = new(null, 0, 0);
}
