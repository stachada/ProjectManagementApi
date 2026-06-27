using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Projects.Queries;

/// <summary>
/// Returns the full detail view of a single board, including a capped list
/// of the most recently created tasks (up to <see cref="BoardDto.MaxEmbeddedTasks"/>).
/// For paginated task access use <see cref="GetBoardTasks"/>.
/// </summary>
/// <param name="BoardId">The board to retrieve.</param>
public sealed record GetBoardById(Guid BoardId) : IQuery<BoardDto>;

/// <summary>
/// Handles <see cref="GetBoardById"/>.
/// Fetches the board, counts all tasks, and loads a capped task page in
/// parallel. Assignee display names are resolved via a single batch lookup.
/// </summary>
public sealed class GetBoardByIdHandler(IAppDbContext db)
    : IQueryHandler<GetBoardById, BoardDto>
{
    public async Task<BoardDto> HandleAsync(
        GetBoardById query,
        CancellationToken cancellationToken = default)
    {
        Board board = await db.Boards
            .SingleOrDefaultAsync(b => b.Id == query.BoardId, cancellationToken)
            ?? throw new NotFoundException(nameof(Board), query.BoardId);

        // Count all tasks for the TaskCount field — separate from the capped list.
        var taskCount = await db.Tasks
            .CountAsync(t => t.BoardId == query.BoardId, cancellationToken);

        // Load the capped task list ordered by CreatedAt descending.
        List<ProjectTask> tasks = await db.Tasks
            .Where(t => t.BoardId == query.BoardId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(BoardDto.MaxEmbeddedTasks)
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

        IReadOnlyList<TaskSummaryDto> taskDtos = tasks
            .Select(t => t.ToSummaryDto(userLookup))
            .ToList();

        return board.ToBoardDto(taskCount, taskDtos);
    }
}
