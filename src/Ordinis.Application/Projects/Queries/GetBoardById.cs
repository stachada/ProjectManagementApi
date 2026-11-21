using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
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

        // Load the capped task list ordered by CreatedAt descending. See
        // EntityQueryableExtensions.ThenByStableId for why the tiebreaker is needed.
        List<ProjectTask> tasks = await db.Tasks
            .Where(t => t.BoardId == query.BoardId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByStableId()
            .Take(BoardDto.MaxEmbeddedTasks)
            .ToListAsync(cancellationToken);

        // Assignee names and comment/attachment counts for the embedded tasks only.
        IReadOnlyList<TaskSummaryDto> taskDtos = await tasks.ToSummaryDtosAsync(db, cancellationToken);

        return board.ToBoardDto(taskCount, taskDtos);
    }
}
