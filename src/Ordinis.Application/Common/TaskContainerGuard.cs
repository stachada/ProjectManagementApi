using Microsoft.EntityFrameworkCore;
using Ordinis.Domain.Common;

namespace Ordinis.Application.Common;

/// <summary>
/// Enforces "an archived board or project is read-only" for its tasks. Every task command
/// handler calls <see cref="EnsureWritableAsync"/> before changing a task, its comments or its
/// attachments.
/// </summary>
/// <remarks>
/// <para>
/// <c>ProjectTask</c>, <c>Board</c> and <c>Project</c> are separate aggregates that reference
/// each other by ID only, so the task aggregate cannot see whether its containers are archived
/// and the rule cannot live inside <c>ProjectTask</c> itself. It is checked here, in the
/// Application layer, with one query for both flags.
/// </para>
/// <para>
/// Throws the containers' own existing error codes (<c>project.archived</c>,
/// <c>board.archived</c>) so clients see one vocabulary for "archived". The project is checked
/// first: unarchiving the board would not help while the project is archived.
/// </para>
/// </remarks>
public static class TaskContainerGuard
{
    /// <summary>
    /// Throws a <see cref="DomainException"/> (→ <c>422</c>) if the board, or the project that owns
    /// it, is archived. Does nothing if the board does not exist — the caller's own existence
    /// checks report that.
    /// </summary>
    /// <param name="db">The application database context.</param>
    /// <param name="boardId">The board the task belongs to (or is being created on).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureWritableAsync(IAppDbContext db, Guid boardId, CancellationToken cancellationToken)
    {
        var state = await db.Boards
            .Where(b => b.Id == boardId)
            .Select(b => new
            {
                BoardArchived = b.IsArchived,
                ProjectArchived = db.Projects
                    .Where(p => p.Id == b.ProjectId)
                    .Select(p => p.IsArchived)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return;
        }

        if (state.ProjectArchived)
        {
            throw new DomainException(
                "The task's project is archived. Unarchive the project before changing its tasks.",
                "project.archived");
        }

        if (state.BoardArchived)
        {
            throw new DomainException(
                "The task's board is archived. Unarchive the board before changing its tasks.",
                "board.archived");
        }
    }
}
