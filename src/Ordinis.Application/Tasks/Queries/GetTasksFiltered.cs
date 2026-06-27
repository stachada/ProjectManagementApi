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
/// <strong>User display names:</strong> resolved via a single batch lookup on the
/// assignee IDs present in the returned page — not across the full dataset.
/// </para>
/// </remarks>
internal sealed class GetTasksFilteredHandler(IAppDbContext db) : IQueryHandler<GetTasksFiltered, PagedResult<TaskSummaryDto>>
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        GetTasksFiltered query,
        CancellationToken cancellationToken)
    {
        TaskFilter filter = query.Filter ?? new TaskFilter();
        var pageSize = Math.Min(filter.PageSize, MaxPageSize);
        var page = Math.Max(filter.Page, 1);

        // Start from the base queryable - the global soft-delete filter is applied
        // automatically by EF Core, so deleted tasks never appear here.
        IQueryable<ProjectTask> queryable = db.Tasks
            .Include(t => t.Comments)
            .Include(t => t.Assignee);

        // Each filter is applied only when the corresponding parameter is provided.
        // This produces a single SQL query with only the WHERE clauses that are needed.
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

        // Executed against the filtered (pre-pagination) queryable.
        // This is a separate round-trip but unavoidable for offset pagination.
        var totalCount = await queryable.CountAsync(cancellationToken);

        // Switch on the sort field name; fall back to createdAt for unknown values.
        // Using a switch expression keeps all sort options visible in one place.
        queryable = (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("title", false) => queryable.OrderBy(t => t.Title),
            ("title", true) => queryable.OrderByDescending(t => t.Title),
            ("status", false) => queryable.OrderBy(t => t.Status),
            ("status", true) => queryable.OrderByDescending(t => t.Status),
            ("priority", false) => queryable.OrderBy(t => t.Priority),
            ("priority", true) => queryable.OrderByDescending(t => t.Priority),
            ("dueDate", false) => queryable.OrderBy(t => t.DueDate),
            ("dueDate", true) => queryable.OrderByDescending(t => t.DueDate),
            ("updatedat", false) => queryable.OrderBy(t => t.UpdatedAt),
            ("updatedat", true) => queryable.OrderByDescending(t => t.UpdatedAt),
            // Default createdAt ascending / descending
            (_, false) => queryable.OrderBy(t => t.CreatedAt),
            (_, true) => queryable.OrderByDescending(t => t.CreatedAt)
        };

        // Pagination
        List<ProjectTask> tasks = await queryable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Use display name resolution
        // Collects assignee IDs from this page only - not from the full dataset.
        HashSet<Guid> assigneeIds = tasks
            .Where(t => t.AssigneeId.HasValue)
            .Select(t => t.AssigneeId!.Value)
            .ToHashSet();

        Dictionary<Guid, string> userLookup = assigneeIds.Count > 0
            ? await db.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            : [];

        // Mapping
        var items = tasks
            .Select(t => t.ToSummaryDto(userLookup))
            .ToList();

        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }
}
