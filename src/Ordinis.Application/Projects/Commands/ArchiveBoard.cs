using FluentValidation;
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
/// <param name="IfMatch">
/// The board's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record ArchiveBoard(Guid BoardId, byte[]? IfMatch) : ICommand;

// Handler
public sealed class ArchiveBoardHandler(IAppDbContext db) : ICommandHandler<ArchiveBoard>
{
    public async Task HandleAsync(ArchiveBoard command, CancellationToken cancellationToken = default)
    {
        Board board = await db.Boards
            .SingleOrDefaultAsync(b => b.Id == command.BoardId, cancellationToken)
                ?? throw new NotFoundException(nameof(Board), command.BoardId);

        ConcurrencyGuard.EnsureMatch(board.RowVersion, command.IfMatch, nameof(Board), command.BoardId);

        // Domain enforces: board not already archived.
        board.Archive();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(nameof(Board), command.BoardId, ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="ArchiveBoard"/> commands.
/// </summary>
public sealed class ArchiveBoardValidator : AbstractValidator<ArchiveBoard>
{
    public ArchiveBoardValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
