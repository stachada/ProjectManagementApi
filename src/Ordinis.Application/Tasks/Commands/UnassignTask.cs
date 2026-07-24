using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Removes the current assignee from a task.
/// </summary>
/// <remarks>
/// Throws a <see cref="DomainException"/> (<c>task.already-unassigned</c>) if the task
/// has no current assignee. The API layer maps this to <c>422 Unprocessable Entity</c>.
/// </remarks>
/// <param name="TaskId">ID of the task to unassign.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
/// <param name="IfMatch">
/// The task's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record UnassignTask(
    Guid TaskId,
    Guid RequestedByUserId,
    byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UnassignTask"/> by invoking <see cref="ProjectTask.Unassign"/>.
/// </summary>
internal sealed class UnassignTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<UnassignTask>
{
    public async Task HandleAsync(UnassignTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        ConcurrencyGuard.EnsureMatch(task.RowVersion, command.IfMatch, "Task", command.TaskId);

        task.Unassign(command.RequestedByUserId, timeProvider.GetUtcNow());

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
/// Validates <see cref="UnassignTask"/> commands.
/// </summary>
/// <remarks>
/// Only the <see cref="UnassignTask.IfMatch"/> concurrency token is validated here - the IDs
/// are guaranteed non-empty by route binding and authentication middleware, matching this
/// command's original no-validator design.
/// </remarks>
internal sealed class UnassignTaskValidator : AbstractValidator<UnassignTask>
{
    public UnassignTaskValidator()
    {
        RuleFor(t => t.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
