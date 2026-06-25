using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Transitions a task to a new workflow status.
/// </summary>
/// <remarks>
/// Valid transitions are enforced by the domain's
/// <see cref="ProjectTaskStatusExtensions.CanTransitionTo"/> state machine.
/// An invalid transition throws a domain exception that the global middleware
/// maps to a <c>422 Unprocessable Entity</c> response.
/// </remarks>
/// <param name="TaskId">ID of the task to transition.</param>
/// <param name="NewStatus">Target status.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
public sealed record MoveTask(
    Guid TaskId,
    ProjectTaskStatus NewStatus,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="MoveTask"/> by invoking <see cref="ProjectTask.Move"/>.
/// which enforces transition validity via the state machine before raising
/// a <see cref="TaskMoved"/> domain event.
/// </summary>
/// <remarks>
/// Concurrency is handled the same way as <see cref="UpdateTaskHandler"/>:
/// <c>DbUpdateConcurrencyException</c> is caught and translated to
/// <see cref="ConcurrencyException"/> -> <c>409 Conflict</c>.
/// </remarks>
internal sealed class MoveTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<MoveTask>
{
    public async Task HandleAsync(MoveTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        task.Move(command.NewStatus, command.RequestedByUserId, timeProvider.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                "Task",
                command.TaskId,
                ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="MoveTask"/> commands.
/// </summary>
/// <remarks>
/// Only structural validation is performed (is <see cref="MoveTask.NewStatus"/>
/// a valid enum member?). Whether the transition is <em>allowed</em> from the task's
/// current status is a domain invariant enforced by <see cref="ProjectTask.Move"/>,
/// not by the validator - domain rules belong in the domain.
/// </remarks>
internal sealed class MoveTaskValidator : AbstractValidator<MoveTask>
{
    public MoveTaskValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty();

        RuleFor(x => x.NewStatus)
            .IsInEnum()
            .WithMessage("NewStatus must be a valid ProjectTaskStatus value.");
    }
}
