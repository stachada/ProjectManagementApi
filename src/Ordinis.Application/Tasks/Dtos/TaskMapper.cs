using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Dtos;

/// <summary>
/// Static mapper for converting <see cref="ProjectTask"/> domain objects to DTOs.
/// </summary>
/// <remarks>
/// <para>
/// All methods are pure functions - no database access, no service injection.
/// The handler is responsible for resolving any cross-aggregate data (e.g. user display
/// names) before calling into the mapper.
/// </para>
/// <para>
/// <strong>Design rationale:</strong> display names for assignees and comment authors
/// require crossing the <c>ProjectTask</c> -> <c>User</c> aggregate boundary. Rather than
/// using EF Core navigation properties between aggregates (a DDD anti-pattern), the handler
/// collects all relevant user IDs, executes a single batched <c>WHERE id IN (...)</c> query,
/// and passes the resulting lookup dictionary here. The mapper stays free of I/O conerns.
/// </para>
/// </remarks>
public static class TaskMapper
{
    /// <summary>
    /// Maps a <see cref="ProjectTask"/> to a full <see cref="TaskDto"/>.
    /// </summary>
    /// <param name="task">The task to map. Must not be <see langword="null"/>.</param>
    /// <param name="userLookup">
    /// A dictionary of <c>UserId -> DisplayName</c> for all users referenced by this task
    /// (assignee + all comment authors). Missing keys are handled gracefully - the display
    /// name falls back to an empty string rather than throwing.
    /// </param>
    public static TaskDto ToDto(this ProjectTask task, IReadOnlyDictionary<Guid, string> userLookup)
        => new()
        {
            Id = task.Id,
            BoardId = task.BoardId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            AssigneeDisplayName = task.AssigneeId.HasValue
                ? userLookup.GetValueOrDefault(task.AssigneeId.Value)
                : null,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            // RowVersion encoded as Base64 for ETag support.
            // RowVersion is set by EF Core on save; it will be non-null for any
            // persisted task. The null-coalescing guard keeps the mapper safe
            // in unit tests that use unpersisted task instances.
            ConcurrencyToken = task.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(task.RowVersion)
                : string.Empty,
            Comments = task.Comments
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .ThenByStableId()
                .Select(c => c.ToCommentDto(userLookup))
                .ToList(),
            Attachments = task.Attachments
                .OrderBy(a => a.UploadedAt)
                .ThenByStableId()
                .Select(a => a.ToAttachmentDto())
                .ToList(),
            Links = BuildLinks(task.Id, task.Status),
        };

    /// <summary>
    /// Maps a <see cref="ProjectTask"/> to a lean <see cref="TaskSummaryDto"/> for list responses.
    /// </summary>
    /// <param name="task">The task to map. Must not be <see langword="null"/>.</param>
    /// <param name="details">
    /// The assignee display name and comment/attachment counts, resolved by the handler
    /// (see <c>TaskSummaryQueries.ToSummaryDtosAsync</c>).
    /// </param>
    /// <remarks>
    /// Counts are passed in rather than read from <see cref="ProjectTask.Comments"/> /
    /// <see cref="ProjectTask.Attachments"/>: list queries deliberately don't load those
    /// collections (materializing every comment row just to count it), and reading an
    /// unloaded collection silently yields <c>0</c> instead of failing.
    /// </remarks>
    public static TaskSummaryDto ToSummaryDto(this ProjectTask task, TaskSummaryDetails details)
        => new()
        {
            Id = task.Id,
            BoardId = task.BoardId,
            Title = task.Title,
            Status = task.Status,
            Priority = task.Priority,
            AssigneeId = task.AssigneeId,
            AssigneeDisplayName = task.AssigneeId.HasValue ? details.AssigneeDisplayName : null,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            CommentCount = details.CommentCount,
            AttachmentCount = details.AttachmentCount
        };

    #region Private helpers - not part of the public mapping surface

    /// <summary>
    /// Builds the <c>_links</c> set for a <see cref="TaskDto"/>: <c>self</c>, <c>assign</c>,
    /// and <c>delete</c> are always present. <c>move</c> plus one <c>move:{status}</c> link per
    /// legally reachable status are included only when <paramref name="status"/> has outbound
    /// transitions (empty for <see cref="ProjectTaskStatus.Cancelled"/>) - no point advertising
    /// an action that would fail validation.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> only so <c>Ordinis.Benchmarks</c>' Mapster
    /// configuration produces byte-identical links without duplicating this logic.
    /// </remarks>
    internal static IReadOnlyList<HateoasLink> BuildLinks(Guid taskId, ProjectTaskStatus status)
    {
        var basePath = $"/api/v1/tasks/{taskId}";

        var links = new List<HateoasLink>
        {
            new("self", basePath, "GET"),
            new("assign", $"{basePath}/assign", "POST"),
            new("delete", basePath, "DELETE"),
        };

        IReadOnlySet<ProjectTaskStatus> allowedTransitions = status.GetAllowedTransitions();
        if (allowedTransitions.Count > 0)
        {
            links.Add(new HateoasLink("move", $"{basePath}/move", "POST"));

            foreach (ProjectTaskStatus target in allowedTransitions)
            {
                links.Add(new HateoasLink($"move:{target.ToString().ToLowerInvariant()}", $"{basePath}/move", "POST"));
            }
        }

        return links;
    }

    private static CommentDto ToCommentDto(this Comment comment, IReadOnlyDictionary<Guid, string> userLookup)
        => new()
        {
            Id = comment.Id,
            AuthorId = comment.AuthorId,
            AuthorDisplayName = userLookup.GetValueOrDefault(comment.AuthorId, string.Empty),
            Content = comment.Content,
            IsEdited = comment.IsEdited,
            CreatedAt = comment.CreatedAt,
            // Use the domain's IsEdited flag as the canonical signal rather than
            // comparing timestamps - IsEdited is set explicitly by UpdateContent()
            // and is unambigous even if CreatedAt and UpdatedAt happen to coincide.
            UpdatedAt = comment.IsEdited ? comment.UpdatedAt : null
        };

    private static AttachmentDto ToAttachmentDto(this Attachment attachment)
        => new()
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeInBytes = attachment.SizeInBytes,
            DownloadUrl = attachment.StorageUrl, // Application layer resolves to pre-signed URL before returning to API consumers
            UploadedAt = attachment.UploadedAt

        };
    #endregion
}
