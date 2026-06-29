using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Renames a board. The new name must be unique within the project
/// (case-insensitive) - enforced by the validator and as a domain backstop
/// via <see cref="Board.Rename"/>.
/// </summary>
/// <param name="BoardId">The board to rename.</param>
/// <param name="NewName">The new display name.</param>
public sealed record RenameBoard(Guid BoardId, string NewName) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RenameBoard"/>.
/// Loads the board directly (not through the project aggregate) because
/// <see cref="Board.Rename"/> carries no project-level invariant - the
/// duplicate name check runs in <see cref="RenameBoardValidator"/> before
/// the handler is invoked. This avoids loading the full board collection
/// just to rename one entry.
/// </summary>
public sealed class RenameBoardHandler(IAppDbContext db) : ICommandHandler<RenameBoard>
{
    public async Task HandleAsync(RenameBoard command, CancellationToken cancellationToken = default)
    {
        Board board = await db.Boards
            .SingleOrDefaultAsync(b => b.Id == command.BoardId, cancellationToken)
                ?? throw new NotFoundException(nameof(Board), command.BoardId);

        board.Rename(command.NewName);

        await db.SaveChangesAsync(cancellationToken);
    }
}

// Validator
/// <summary>
/// Validates <see cref="RenameBoard"/> before the handler runs.
/// The duplicate name check is scoped to the board's project - resolved
/// via the board's <c>ProjectId</c> FK without loading the full project.
/// </summary>
public sealed class RenameBoardValidator : AbstractValidator<RenameBoard>
{
    public RenameBoardValidator(IAppDbContext db)
    {
        RuleFor(x => x.BoardId)
            .NotEmpty();

        RuleFor(x => x.NewName)
            .NotEmpty()
            .MaximumLength(100)
            .MustAsync(async (command, newName, ct) =>
            {
                // Fetch the board's ProjectId without loading the full aggregate.
                Guid? projectId = await db.Boards
                    .Where(b => b.Id == command.BoardId)
                    .Select(b => (Guid?)b.ProjectId)
                    .SingleOrDefaultAsync(ct);

                if (projectId is null)
                {
                    // Board not found - NotFoundException will fire in the handler.
                    return true;
                }

                return !await db.Boards.AnyAsync(
                    b => b.ProjectId == projectId
                        && b.Id != command.BoardId
                        && b.Name.ToLower() == newName.Trim().ToLower(), ct);
            })
            .WithMessage("A board with this name already exists in the project.");
    }
}
