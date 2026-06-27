using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Projects.Queries;

// Query
/// <summary>
/// Returns paginated, filterable list of tasks for a specific board.
/// This is the backing query for <c>GET /api/v1/boards/{id}/tasks</c>
/// and handles the full dataset that <see cref="GetBoardById"/> caps at
/// <see cref="BoardDto.MaxEmbeddedTasks"/>.
/// </summary>
/// <param name="BoardId">The board whose tasks to list.</param>
/// <param name="Filter">Optional filter, sort, and pagination parameters.</param>
public sealed record GetBoardTasks(
    Guid BoardId,
    TaskFilter? Filter = null) : IQuery<PagedResult<TaskSummaryDto>>;

// Handler
/// <summary>
/// Handles <see cref="GetBoardTasks"/>.
/// Applies a board-scoped predicate then the standard <see cref="TaskFilter"/>
/// chain - the same composable pattern used by <see cref="GetProjectTasksHandler"/>.
/// </summary>
public sealed class GetBoardTasksHandler(IAppDbContext db)
    : IQueryHandler<GetBoardTasks, PagedResult<TaskSummaryDto>>
{
    public async Task<PagedResult<TaskSummaryDto>> HandleAsync(
        GetBoardTasks query,
        CancellationToken cancellationToken = default)
    {
        var boardExists = await db.Boards
            .AnyAsync(b => b.Id == query.BoardId, cancellationToken);

        if (!boardExists)
        {
            throw new NotFoundException(nameof(Board), query.BoardId);
        }

        TaskFilter filter = query.Filter ?? new TaskFilter();
        var pageSize = Math.Min(filter.PageSize, 100);
        var page = Math.Max(filter.Page, 1);

        IQueryable<ProjectTask> q = db.Tasks
            .Where(t => t.BoardId == query.BoardId);

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
            q = q.Where(t => t.DueDate <= filter.DueBefore);
        }

        if (filter.DueAfter.HasValue)
        {
            q = q.Where(t => t.DueDate >= filter.DueAfter);
        }

        q = (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("title", false) => q.OrderBy(t => t.Title),
            ("title", true) => q.OrderByDescending(t => t.Title),
            ("priority", false) => q.OrderBy(t => t.Priority),
            ("priority", true) => q.OrderByDescending(t => t.Priority),
            ("dueDate", false) => q.OrderBy(t => t.DueDate),
            ("dueDate", true) => q.OrderByDescending(t => t.DueDate),
            (_, false) => q.OrderBy(t => t.CreatedAt),
            (_, true) => q.OrderByDescending(t => t.CreatedAt)
        };

        var totalCount = await q.CountAsync(cancellationToken);

        List<ProjectTask> tasks = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

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
