using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Projects.Queries;

/// <summary>
/// Returns a paginated list of all tasks across all boards in a project,
/// respecting the standard <see cref="TaskFilter"/> parameters.
/// Semantically distinct from <see cref="GetTasksFiltered"/> — this query
/// is project-scoped and is the backing query for
/// <c>GET /api/v1/projects/{id}/tasks</c>.
/// </summary>
/// <param name="ProjectId">The project whose tasks to list.</param>
/// <param name="Filter">Optional filter, sort, and pagination parameters.</param>
public sealed record GetProjectTasks(
    Guid ProjectId,
    TaskFilter? Filter = null) : IQuery<PagedResult<TaskSummaryDto>>;

/// <summary>
/// Handles <see cref="GetProjectTasks"/>.
/// Applies a project-scoped join (<c>task.Board.ProjectId == projectId</c>)
/// before applying the standard <see cref="TaskFilter"/> predicate chain.
/// This keeps <see cref="TaskFilter"/> clean (no <c>ProjectId?</c> field)
/// while reusing all filter/sort/page logic.
/// </summary>
public sealed class GetProjectTasksHandler(IAppDbContext db)
    : IQueryHandler<GetProjectTasks, PagedResult<TaskSummaryDto>>
{
    public async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        GetProjectTasks query,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects
            .AnyAsync(p => p.Id == query.ProjectId, cancellationToken);

        if (!projectExists)
        {
            throw new NotFoundException(nameof(Project), query.ProjectId);
        }

        var filter = query.Filter ?? new TaskFilter();
        var pageSize = Math.Min(filter.PageSize, 100);
        var page = Math.Max(filter.Page, 1);

        // Scope to the project via the Board → ProjectId FK.
        IQueryable<Domain.Tasks.ProjectTask> q = db.Tasks
            .Where(t => t.Board!.ProjectId == query.ProjectId);

        // Apply standard task filters on top of the project scope.
        if (filter.BoardId.HasValue)
        {
            q = q.Where(t => t.BoardId == filter.BoardId.Value);
        }

        if (filter.AssigneeId.HasValue)
        {
            q = q.Where(t => t.AssigneeId == filter.AssigneeId.Value);
        }

        if (filter.Status.HasValue)
        {
            q = q.Where(t => t.Status == filter.Status.Value);
        }

        if (filter.Priority.HasValue)
        {
            q = q.Where(t => t.Priority == filter.Priority.Value);
        }

        if (filter.DueBefore.HasValue)
        {
            q = q.Where(t => t.DueDate <= filter.DueBefore.Value);
        }

        if (filter.DueAfter.HasValue)
        {
            q = q.Where(t => t.DueDate >= filter.DueAfter.Value);
        }

        q = (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("title", false) => q.OrderBy(t => t.Title),
            ("title", true) => q.OrderByDescending(t => t.Title),
            ("priority", false) => q.OrderBy(t => t.Priority),
            ("priority", true) => q.OrderByDescending(t => t.Priority),
            ("duedate", false) => q.OrderBy(t => t.DueDate),
            ("duedate", true) => q.OrderByDescending(t => t.DueDate),
            (_, false) => q.OrderBy(t => t.CreatedAt),
            (_, true) => q.OrderByDescending(t => t.CreatedAt)
        };

        var totalCount = await q.CountAsync(cancellationToken);

        List<ProjectTask> tasks = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Batch-resolve assignee display names for this page only.
        var assigneeIds = tasks
            .Where(t => t.AssigneeId.HasValue)
            .Select(t => t.AssigneeId!.Value)
            .ToHashSet();

        Dictionary<Guid, string> userLookup = assigneeIds.Count > 0
            ? await db.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            : [];

        var items = tasks.Select(t => t.ToSummaryDto(userLookup)).ToList();

        return new PagedResult<TaskSummaryDto>(items, totalCount, page, pageSize);
    }
}
