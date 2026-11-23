using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Queries;

// Query
/// <summary>
/// Returns a paginated, filtered list of tasks assigned to a specific user.
/// </summary>
/// <param name="UserId">The user whose assigned tasks to retrieve.</param>
/// <param name="Filter">
/// Optional filter, sort, and pagination parameters.
/// <c>AssigneeId</c> on the filter is ignored — it is always overridden with
/// <paramref name="UserId"/> to scope the query correctly.
/// </param>
public sealed record GetUserTasks(Guid UserId, TaskFilter? Filter = null) : IQuery<PagedResult<TaskSummaryDto>>;

// Handler
/// <summary>
/// Handles <see cref="GetUserTasks"/> queries.
/// </summary>
/// <remarks>
/// <para>
/// Applies the same EF Core predicate chain as <c>GetTasksFilteredHandler</c>
/// but with <c>AssigneeId</c> pre-scoped to <see cref="GetUserTasks.UserId"/>.
/// The logic is intentionally duplicated here rather than coupling handlers
/// through the dispatcher — the query is small and the duplication is minimal.
/// </para>
/// <para>
/// The handler verifies the user exists before running the task query, so
/// a missing user returns a clean <see cref="NotFoundException"/> (404) rather
/// than an empty list that could be mistaken for "user has no tasks".
/// </para>
/// <para>
/// Results are restricted to projects the <i>caller</i> can see (see
/// <see cref="CurrentUserServiceExtensions.GetAllowedProjectIds"/>) - the target user may be
/// assigned tasks in projects the caller has no access to, and listing them here would leak
/// those tasks' titles, statuses, and due dates.
/// </para>
/// </remarks>
internal sealed class GetUserTasksHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IQueryHandler<GetUserTasks, PagedResult<TaskSummaryDto>>
{
    public async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        GetUserTasks query,
        CancellationToken ct)
    {
        // Verify the user exists — empty result vs not-found are distinct.
        var userExists = await db.Users
            .AnyAsync(u => u.Id == query.UserId, ct);

        if (!userExists)
            throw new NotFoundException(nameof(User), query.UserId);

        TaskFilter filter = query.Filter ?? new TaskFilter();

        PageRequest paging = PageRequest.Normalize(filter.Page, filter.PageSize);
        var page = paging.Page;
        var pageSize = paging.PageSize;

        // Build the base queryable — AssigneeId always scoped to this user.
        IQueryable<ProjectTask> queryable = db.Tasks
            .AsNoTracking()
            .Where(t => t.AssigneeId == query.UserId);

        // Apply optional additional filters from the caller.
        if (filter.BoardId.HasValue)
        {
            queryable = queryable.Where(t => t.BoardId == filter.BoardId.Value);
        }

        if (filter.Status.HasValue)
        {
            queryable = queryable.Where(t => t.Status == filter.Status.Value);
        }

        if (filter.Priority.HasValue)
        {
            queryable = queryable.Where(t => t.Priority == filter.Priority.Value);
        }

        if (filter.DueBefore.HasValue)
        {
            queryable = queryable.Where(t => t.DueDate <= filter.DueBefore.Value);
        }

        if (filter.DueAfter.HasValue)
        {
            queryable = queryable.Where(t => t.DueDate >= filter.DueAfter.Value);
        }

        IQueryable<Guid> allowedProjectIds = currentUser.GetAllowedProjectIds(db);
        queryable = queryable.Where(t => db.Boards
            .Any(b => b.Id == t.BoardId && allowedProjectIds.Contains(b.ProjectId)));

        // Total count on the filtered (pre-pagination) queryable.
        var totalCount = await queryable.CountAsync(ct);

        // Sorting: an unknown field falls back to createdAt in the requested direction, like every
        // other task list. See EntityQueryableExtensions.ThenByStableId for why every branch ends with it.
        queryable = filter.SortBy?.ToLowerInvariant() switch
        {
            "title" => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.Title).ThenByStableId()
                                : queryable.OrderBy(t => t.Title).ThenByStableId(),
            // Priority is stored as a string: sort by severity (enum order), not alphabetically.
            "priority" => queryable.OrderByEnumValue(t => t.Priority, filter.SortDescending).ThenByStableId(),
            "duedate" => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.DueDate).ThenByStableId()
                                : queryable.OrderBy(t => t.DueDate).ThenByStableId(),
            _ => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.CreatedAt).ThenByStableId()
                                : queryable.OrderBy(t => t.CreatedAt).ThenByStableId(),
        };

        // Paginate and materialise.
        List<ProjectTask> tasks = await queryable
            .Skip(paging.Skip)
            .Take(pageSize)
            .ToListAsync(ct);

        // Assignee names and comment/attachment counts for the current page only.
        List<TaskSummaryDto> items = await tasks.ToSummaryDtosAsync(db, ct);

        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }
}
