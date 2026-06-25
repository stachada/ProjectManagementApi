using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Queries;

// Query
/// <summary>
/// Returns the full detail view of a single task, including its comments and attachments.
/// </summary>
/// <param name="TaskId">ID of the task to retrieve.</param>
public sealed record GetTaskById(Guid TaskId) : IQuery<TaskDto>;

// Handler
/// <summary>
/// Handles <see cref="GetTaskById"/> by loading the task with its child collections
/// and resolving all referenced user display names via a single batched query.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Cross-aggregate user resolution:</strong>
/// <see cref="Comment.AuthorId"/> and <see cref="ProjectTask.AssigneeId"/> reference
/// the <c>User</c> aggregate by ID. Rather than using EF Core navigation properties
/// between aggregates (which would create coupling between aggregate boundaries),
/// the handler collects all relevant user IDs into a <see cref="HashSet{T}"/>,
/// issues one <c>WHERE id IN (...)</c> query, and passes the resulting dictionary
/// to <see cref="TaskMapper.ToDto"/>. The mapper is a pure function — no I/O.
/// </para>
/// <para>
/// This produces exactly two database round-trips:
/// <list type="number">
///   <item>Load task + Comments + Attachments (one query with two JOINs).</item>
///   <item>Load display names for all referenced users (one IN query).</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class GetTaskByIdHandler(IAppDbContext db) : IQueryHandler<GetTaskById, TaskDto>
{
    public async Task<TaskDto> HandleAsync(GetTaskById query, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Attachments)
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == query.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), query.TaskId);

        // Collect all user IDs referenced by this task in a single pass.
        // Using a HashSet deduplicates IDs efficiently — the assignee may also
        // be a comment author, and comments may share authors.
        var userIds = new HashSet<Guid>();

        if (task.AssigneeId.HasValue)
        {
            userIds.Add(task.AssigneeId.Value);
        }

        foreach (var comment in task.Comments)
        {
            userIds.Add(comment.AuthorId);
        }

        // Single batch lookup - one IN query regardless of how many users are referenced.
        Dictionary<Guid, string> userLookup = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            : [];

        return task.ToDto(userLookup);
    }
}
