using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Archives a board, making it read-only.
/// <see cref="Board"/> is an independent aggregate root, so this loads the
/// board directly — no project-level invariant applies.
/// </summary>
/// <param name="BoardId">The board to archive.</param>
public sealed record ArchiveBoard(Guid BoardId) : ICommand;

// Handler
public sealed class ArchiveBoardHandler(IAppDbContext db) : ICommandHandler<ArchiveBoard>
{
    public async Task HandleAsync(ArchiveBoard command, CancellationToken cancellationToken = default)
    {
        Board board = await db.Boards
            .SingleOrDefaultAsync(b => b.Id == command.BoardId, cancellationToken)
                ?? throw new NotFoundException(nameof(Board), command.BoardId);

        // Domain enforces: board not already archived.
        board.Archive();

        await db.SaveChangesAsync(cancellationToken);
    }
}
