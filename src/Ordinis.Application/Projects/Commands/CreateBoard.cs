using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Creates a new board within the specified project.
/// The project-exists/not-archived check and the duplicate-name invariant
/// are both cross-aggregate concerns enforced by <see cref="CreateBoardValidator"/>
/// before the handler runs.
/// </summary>
/// <param name="ProjectId">The project to add the board to.</param>
/// <param name="CreatedByUserId">The user creating the board.</param>
/// <param name="Name">Display name. Must be unique within the project (case-insensitive).</param>
public sealed record CreateBoard(Guid ProjectId, Guid CreatedByUserId, string Name) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="CreateBoard"/>.
/// Creates the board directly — <see cref="Board"/> is an independent
/// aggregate root with no need to load the owning <see cref="Project"/>.
/// </summary>
public sealed class CreateBoardHandler(IAppDbContext db) : ICommandHandler<CreateBoard, Guid>
{
    public async Task<Guid> HandleAsync(CreateBoard command, CancellationToken cancellationToken = default)
    {
        Board board = Board.Create(command.ProjectId, command.Name, command.CreatedByUserId);

        db.Boards.Add(board);
        await db.SaveChangesAsync(cancellationToken);

        return board.Id;
    }
}

/// <summary>
/// Validates <see cref="CreateBoard"/> before the handler runs.
/// <see cref="Board"/> has no visibility into the owning project or its
/// sibling boards, so the project-exists/not-archived check and the
/// duplicate-name check are both enforced here rather than in the domain.
/// </summary>
public sealed class CreateBoardValidator : AbstractValidator<CreateBoard>
{
    public CreateBoardValidator(IAppDbContext db)
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await db.Projects.AnyAsync(p => p.Id == id && !p.IsArchived, ct))
            .WithMessage("Project not found or is archived.");

        RuleFor(x => x.CreatedByUserId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .MustAsync(async (command, name, ct) =>
                !await db.Boards.AnyAsync(
                    b => b.ProjectId == command.ProjectId
                        && b.Name.ToLower() == name.Trim().ToLower(), ct))
            .WithMessage("A board with this name already exists in the project.");
    }
}
