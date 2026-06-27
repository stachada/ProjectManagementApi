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
/// </remarks>
internal sealed class GetUserTasksHandler(IAppDbContext db)
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

        var page     = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

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

        // Total count on the filtered (pre-pagination) queryable.
        var totalCount = await queryable.CountAsync(ct);

        // Sorting.
        queryable = filter.SortBy?.ToLowerInvariant() switch
        {
            "title"      => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.Title)
                                : queryable.OrderBy(t => t.Title),
            "priority"   => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.Priority)
                                : queryable.OrderBy(t => t.Priority),
            "duedate"    => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.DueDate)
                                : queryable.OrderBy(t => t.DueDate),
            "createdat"  => filter.SortDescending
                                ? queryable.OrderByDescending(t => t.CreatedAt)
                                : queryable.OrderBy(t => t.CreatedAt),
            _            => queryable.OrderByDescending(t => t.CreatedAt), // default
        };

        // Paginate and materialise.
        List<ProjectTask> tasks = await queryable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Resolve assignee display names for the current page only.
        var assigneeIds = tasks
            .Where(t => t.AssigneeId.HasValue)
            .Select(t => t.AssigneeId!.Value)
            .ToHashSet();

        Dictionary<Guid, string> userLookup = assigneeIds.Count > 0
            ? await db.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct)
            : [];

        var items = tasks.Select(t => t.ToSummaryDto(userLookup)).ToList();

        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }
}
