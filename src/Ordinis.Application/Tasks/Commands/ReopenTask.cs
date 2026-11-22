using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Reopens a <see cref="ProjectTaskStatus.Done"/> task, returning it to <see cref="ProjectTaskStatus.ToDo"/>.
/// </summary>
/// <remarks>
/// Unlike a plain <see cref="MoveTask"/> to ToDo, this is rejected for any task that is not
/// Done — see <see cref="ProjectTask.Reopen"/>.
/// </remarks>
/// <param name="TaskId">ID of the task to reopen.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
/// <param name="IfMatch">
/// The task's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record ReopenTask(
    Guid TaskId,
    Guid RequestedByUserId,
    byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="ReopenTask"/> by invoking <see cref="ProjectTask.Reopen"/>.
/// </summary>
/// <remarks>
/// Same shape as <see cref="MoveTaskHandler"/>: not-found → 404, stale <c>If-Match</c> → 409,
/// archived board/project → 422, and <c>DbUpdateConcurrencyException</c> → 409.
/// </remarks>
internal sealed class ReopenTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<ReopenTask>
{
    public async Task HandleAsync(ReopenTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        ConcurrencyGuard.EnsureMatch(task.RowVersion, command.IfMatch, "Task", command.TaskId);

        // Archived board/project = read-only tasks (see TaskContainerGuard).
        await TaskContainerGuard.EnsureWritableAsync(db, task.BoardId, cancellationToken);

        task.Reopen(command.RequestedByUserId, timeProvider.GetUtcNow());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Task", command.TaskId, ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="ReopenTask"/> commands. Whether the task is Done is a domain invariant
/// enforced by <see cref="ProjectTask.Reopen"/>, not here.
/// </summary>
internal sealed class ReopenTaskValidator : AbstractValidator<ReopenTask>
{
    public ReopenTaskValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty();

        RuleFor(x => x.RequestedByUserId)
            .NotEmpty();

        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
