using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Creates a new task on the specified board.
/// </summary>
/// <param name="BoardId">The board the task will be created on.</param>
/// <param name="Title">Short title for the task (max 200 characters).</param>
/// <param name="Description">Optional long-form description.</param>
/// <param name="Priority">Initial priority. Defaults to <see cref="Priority.Medium"/>.</param>
/// <param name="AssigneeId">Optional user to immediately assign the task to.</param>
/// <param name="DueDate">Optional due date.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command, used for domain event attribution.</param>
public sealed record CreateTask(
    Guid BoardId,
    string Title,
    string? Description,
    Priority Priority,
    Guid? AssigneeId,
    DateTimeOffset? DueDate,
    Guid RequestedByUserId
) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="CreateTask"/> by creating a new <see cref="ProjectTask"/>
/// aggregate and persisting it.
/// </summary>
/// <remarks>
/// <para>
/// The handler resolves <c>now</c> form its injected <see cref="TimeProvider"/>
/// and passes it into the domain factory method - in line with the project-wide
/// rule that no domain or infracstructure code calls <c>DateTimeOffset.UtcNow</c> directly.
/// </para>
/// <para>
/// <strong>Board existence:</strong> validated by <see cref="CreateTaskValidator"/> before
/// this handler is invoked, so no defensive re-check is needed here.
/// </para>
/// </remarks>
/// <param name="db"></param>
/// <param name="timeProvider"></param>
internal sealed class CreateTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider): ICommandHandler<CreateTask, Guid>
{
    public async Task<Guid> HandleAsync(CreateTask command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        var task = ProjectTask.Create(
            boardId: command.BoardId,
            reporterId: command.RequestedByUserId,
            title: command.Title,
            now: now,
            priority: command.Priority,
            description: command.Description,
            dueDate: command.DueDate);

        // Assign immediately if an assignee was supplied with the creation request.
        // This raises a TaskAssigned domain event in addition to TaskCreated.
        if (command.AssigneeId is not null)
        {
            task.Assign(
                assigneeId: command.AssigneeId.Value,
                assignedByUserId: command.RequestedByUserId,
                now: now);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return task.Id;
    }
}

// Validator
/// <summary>
/// Validates <see cref="CreateTask"/> commands before the handler is invoked.
/// </summary>
/// <remarks>
/// Board existence is verified asynchronously against the database. If the board
/// does not exist or has been archived, the command is rejected here rather than
/// allowing the handler to produce a foreign-key violation.
/// </remarks>
internal sealed class CreateTaskValidator : AbstractValidator<CreateTask>
{
    public CreateTaskValidator(IAppDbContext db)
    {
        RuleFor(x => x.BoardId)
            .NotEmpty()
            .MustAsync(async (boardId, ct) =>
                await db.Boards.AnyAsync(b => b.Id == boardId && !b.IsArchived, ct))
            .WithMessage("Board does not exist or has been archived.");

        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Priority)
            .IsInEnum()
            .WithMessage("Priority must be a valid Priority value.");

        // AssigneeId is optional at creation; when provided, existence is verified
        // Project-membership check is intentionally omitted here - that policy
        // is enforced at the authorization layer, not in the domain command.
        RuleFor(x => x.AssigneeId)
            .MustAsync(async (assigneeId, ct) =>
                await db.Users.AnyAsync(u => u.Id == assigneeId, ct))
            .When(x => x.AssigneeId.HasValue)
            .WithMessage("Assignee user does not exist.");

        RuleFor(x => x.RequestedByUserId)
            .NotEmpty();
    }
}
