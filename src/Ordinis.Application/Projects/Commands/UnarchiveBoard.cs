using FluentValidation;
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
/// <param name="IfMatch">
/// The board's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record UnarchiveBoard(Guid BoardId, byte[]? IfMatch) : ICommand;

// Handler
public sealed class UnarchiveBoardHandler(IAppDbContext db) : ICommandHandler<UnarchiveBoard>
{
    public async Task HandleAsync(UnarchiveBoard command, CancellationToken cancellationToken = default)
    {
        Board board = await db.Boards
            .SingleOrDefaultAsync(b => b.Id == command.BoardId, cancellationToken)
                ?? throw new NotFoundException(nameof(Board), command.BoardId);

        ConcurrencyGuard.EnsureMatch(board.RowVersion, command.IfMatch, nameof(Board), command.BoardId);

        // Domain enforces: board is currently archived.
        board.Unarchive();

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
/// Validates <see cref="UnarchiveBoard"/> commands.
/// </summary>
public sealed class UnarchiveBoardValidator : AbstractValidator<UnarchiveBoard>
{
    public UnarchiveBoardValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
