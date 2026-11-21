using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;

namespace Ordinis.Benchmarks.Queries;

/// <summary>
/// EF Core variant of <c>GetTasksFilteredHandler</c> that projects straight into
/// <see cref="TaskSummaryDto"/> with <c>Select</c> instead of materializing tracked
/// <see cref="ProjectTask"/> aggregates with <c>Include(Comments)</c>.
/// </summary>
/// <remarks>
/// <para>
/// This answers the question the EF-vs-Dapper comparison raises: how much of the gap is EF Core
/// itself, and how much is the handler's query shape? Filters, caller scoping and ordering are
/// copied verbatim from the production handler; only the materialization differs:
/// </para>
/// <list type="bullet">
///   <item>Comment and attachment counts become SQL <c>COUNT</c> subqueries (the global
///   soft-delete filters still apply), so no comment rows are loaded just to be counted.</item>
///   <item>The assignee display name is a correlated subquery on <c>Users</c> by FK, which
///   removes both the second user-lookup round-trip and the cross-aggregate
///   <c>Include(t =&gt; t.Assignee)</c> navigation.</item>
///   <item>No change tracking - projections are never tracked.</item>
/// </list>
/// </remarks>
internal static class EfProjectionGetTasksFiltered
{
    private const int MaxPageSize = 100;

    public static async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        IAppDbContext db,
        TaskFilter filter,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        int pageSize = Math.Min(filter.PageSize, MaxPageSize);
        int page = Math.Max(filter.Page, 1);

        IQueryable<ProjectTask> queryable = db.Tasks;

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

        // Inlined copy of CurrentUserServiceExtensions.GetAllowedProjectIds (internal to Application).
        IQueryable<Guid> allowedProjectIds = currentUser.IsOrgAdmin
            ? db.Projects.Where(p => p.OrganizationId == currentUser.OrganizationId).Select(p => p.Id)
            : db.ProjectMembers.Where(m => m.UserId == currentUser.UserId).Select(m => m.ProjectId);

        queryable = queryable.Where(t => db.Boards
            .Any(b => b.Id == t.BoardId && allowedProjectIds.Contains(b.ProjectId)));

        int totalCount = await queryable.CountAsync(cancellationToken);

        // Deferred join (same reasoning as DapperGetTasksFiltered): page the IDs first, then
        // project - and re-sort - only those rows, so the COUNT subqueries run per page row
        // rather than for every row SQL Server walks past to reach the offset.
        IQueryable<Guid> pageIds = ApplySort(queryable, filter)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => t.Id);

        List<TaskSummaryDto> items = await ApplySort(db.Tasks.Where(t => pageIds.Contains(t.Id)), filter)
            .Select(t => new TaskSummaryDto
            {
                Id = t.Id,
                BoardId = t.BoardId,
                Title = t.Title,
                Status = t.Status,
                Priority = t.Priority,
                AssigneeId = t.AssigneeId,
                AssigneeDisplayName = db.Users
                    .Where(u => u.Id == t.AssigneeId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                CommentCount = t.Comments.Count(c => !c.IsDeleted),
                AttachmentCount = t.Attachments.Count,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }

    private static IQueryable<ProjectTask> ApplySort(IQueryable<ProjectTask> queryable, TaskFilter filter)
        => (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("title", false) => queryable.OrderBy(t => t.Title).ThenByStableId(),
            ("title", true) => queryable.OrderByDescending(t => t.Title).ThenByStableId(),
            ("status", false) => queryable.OrderBy(t => t.Status).ThenByStableId(),
            ("status", true) => queryable.OrderByDescending(t => t.Status).ThenByStableId(),
            ("priority", false) => queryable.OrderBy(t => t.Priority).ThenByStableId(),
            ("priority", true) => queryable.OrderByDescending(t => t.Priority).ThenByStableId(),
            ("duedate", false) => queryable.OrderBy(t => t.DueDate).ThenByStableId(),
            ("duedate", true) => queryable.OrderByDescending(t => t.DueDate).ThenByStableId(),
            ("updatedat", false) => queryable.OrderBy(t => t.UpdatedAt).ThenByStableId(),
            ("updatedat", true) => queryable.OrderByDescending(t => t.UpdatedAt).ThenByStableId(),
            (_, false) => queryable.OrderBy(t => t.CreatedAt).ThenByStableId(),
            (_, true) => queryable.OrderByDescending(t => t.CreatedAt).ThenByStableId(),
        };
}
