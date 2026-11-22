using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Queries;

// Filter
/// <summary>
/// Filter criteria for <see cref="GetTasksFiltered"/>. All members are optional -
/// omitting them returns all visible tasks.
/// </summary>
/// <param name="BoardId">Filter to tasks on a specific board.</param>
/// <param name="AssigneeId">Filter to tasks assigned to a specific user.</param>
/// <param name="Status">Filter to tasks in a specific workflow status.</param>
/// <param name="Priority">Filter to tasks with a specific priority.</param>
/// <param name="DueBefore">Filter to tasks with a due date on or before this timestamp.</param>
/// <param name="DueAfter">Filter to tasks with a due date on or after this timestamp.</param>
public sealed record TaskFilter(
    string? Search = null,
    Guid? BoardId = null,
    Guid? AssigneeId = null,
    ProjectTaskStatus? Status = null,
    Priority? Priority = null,
    DateTimeOffset? DueBefore = null,
    DateTimeOffset? DueAfter = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "createdAt",
    bool SortDescending = false);

// Query
/// <summary>
/// Returns a paginated, filtered, and sorted list of tasks.
/// </summary>
/// <param name="Filter">Filter criteria. Defaults to no filtering.</param>
/// <param name="Page">1-based page numbrer. Defaults to 1.</param>
/// <param name="PageSize">Items per page. Defaults t0 20, capped at 100.</param>
/// <param name="SortBy">
/// Field name to sort by. Supported values: <c>title</c>, <c>status</c>,
/// <c>priority</c>, <c>dueDate</c>, <c>updatedAt</c>, <c>createdAt</c>.
/// Defaults to <c>createdAt</c>.
/// </param>
/// <param name="SortDescending">When <see langword="true"/>, results are sorted descending.</param>
public sealed record GetTasksFiltered(TaskFilter? Filter = null) : IQuery<PagedResult<TaskSummaryDto>>;

// Handler
/// <summary>
/// Handles <see cref="GetTasksFiltered"/> using EF Core LINQ with progressive
/// <c>IQueryable</c> composition.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why EF Core LINQ instead of Dapper here:</strong>
/// This is a filtered list with simple equality and range predicates — exactly the
/// case where EF Core's <c>IQueryable</c> chaining produces clean, maintainable SQL.
/// Dapper is reserved for reporting queries that require aggregations or multi-table
/// joins that are awkward to express in LINQ.
/// </para>
/// <para>
/// <strong>Pagination strategy:</strong> offset-based (<c>Skip/Take</c>). The
/// <c>TotalCount</c> is fetched with a separate <c>COUNT(*)</c> query on the same
/// filtered <c>IQueryable</c> — before pagination is applied. Both queries share
/// the same predicate chain, so they always return consistent counts.
/// </para>
/// <para>
/// <strong>Display names and counts:</strong> resolved by
/// <see cref="TaskSummaryQueries.ToSummaryDtosAsync"/> in one batched query over the returned
/// page's IDs — not across the full dataset.
/// </para>
/// </remarks>
internal sealed class GetTasksFilteredHandler(IAppDbContext db, ICurrentUserService currentUser) : IQueryHandler<GetTasksFiltered, PagedResult<TaskSummaryDto>>
{

    public async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        GetTasksFiltered query,
        CancellationToken cancellationToken)
    {
        TaskFilter filter = query.Filter ?? new TaskFilter();
        PageRequest paging = PageRequest.Normalize(filter.Page, filter.PageSize);
        var page = paging.Page;
        var pageSize = paging.PageSize;

        // Start from the base queryable - the global soft-delete filter is applied
        // automatically by EF Core, so deleted tasks never appear here. No Include: comment and
        // attachment counts and the assignee name are resolved per page by ToSummaryDtosAsync.
        IQueryable<ProjectTask> queryable = db.Tasks;

        // Each filter is applied only when the corresponding parameter is provided.
        // This produces a single SQL query with only the WHERE clauses that are needed.
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim().ToLower();
            queryable = queryable.Where(
                t => t.Title.ToLower().Contains(search)
                || (t.Description != null && t.Description.ToLower().Contains(search)));
        }

        if (filter.BoardId.HasValue)
        {
            queryable = queryable.Where(t => t.BoardId == filter.BoardId.Value);
        }

        if (filter.AssigneeId.HasValue)
        {
            queryable = queryable.Where(t => t.AssigneeId == filter.AssigneeId.Value);
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

        // Executed against the filtered (pre-pagination) queryable.
        // This is a separate round-trip but unavoidable for offset pagination.
        var totalCount = await queryable.CountAsync(cancellationToken);

        // Switch on the sort field name; fall back to createdAt for unknown values.
        // Using a switch expression keeps all sort options visible in one place.
        // See EntityQueryableExtensions.ThenByStableId for why every branch ends with it.
        queryable = (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("title", false) => queryable.OrderBy(t => t.Title).ThenByStableId(),
            ("title", true) => queryable.OrderByDescending(t => t.Title).ThenByStableId(),
            // Status/Priority are stored as strings: sort by declared enum order, not alphabetically.
            ("status", var descending) => queryable.OrderByEnumValue(t => t.Status, descending).ThenByStableId(),
            ("priority", var descending) => queryable.OrderByEnumValue(t => t.Priority, descending).ThenByStableId(),
            // Lowercase: SortBy is lowercased above, so a mixed-case pattern can never match.
            ("duedate", false) => queryable.OrderBy(t => t.DueDate).ThenByStableId(),
            ("duedate", true) => queryable.OrderByDescending(t => t.DueDate).ThenByStableId(),
            ("updatedat", false) => queryable.OrderBy(t => t.UpdatedAt).ThenByStableId(),
            ("updatedat", true) => queryable.OrderByDescending(t => t.UpdatedAt).ThenByStableId(),
            // Default createdAt ascending / descending
            (_, false) => queryable.OrderBy(t => t.CreatedAt).ThenByStableId(),
            (_, true) => queryable.OrderByDescending(t => t.CreatedAt).ThenByStableId()
        };

        // Pagination
        List<ProjectTask> tasks = await queryable
            .Skip(paging.Skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Assignee names and comment/attachment counts for this page only.
        List<TaskSummaryDto> items = await tasks.ToSummaryDtosAsync(db, cancellationToken);

        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }
}
