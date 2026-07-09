using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Restores an archived board to active status.
/// <see cref="Board"/> is an independent aggregate root, so this loads the
/// board directly — no project-level invariant applies.
/// </summary>
/// <param name="BoardId">The board to unarchive.</param>
public sealed record UnarchiveBoard(Guid BoardId) : ICommand;

// Handler
public sealed class UnarchiveBoardHandler(IAppDbContext db) : ICommandHandler<UnarchiveBoard>
{
    public async Task HandleAsync(UnarchiveBoard command, CancellationToken cancellationToken = default)
    {
        Board board = await db.Boards
            .SingleOrDefaultAsync(b => b.Id == command.BoardId, cancellationToken)
                ?? throw new NotFoundException(nameof(Board), command.BoardId);

        // Domain enforces: board is currently archived.
        board.Unarchive();

        await db.SaveChangesAsync(cancellationToken);
    }
}
