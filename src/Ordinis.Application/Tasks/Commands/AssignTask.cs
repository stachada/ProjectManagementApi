using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Assigns a task to the specified user.
/// </summary>
/// <remarks>
/// If the task is already assigned to a different user, the aggregate raises
/// a <see cref="TaskUnassigned"/> event for the previous assignee and a
/// <see cref="TaskAssigned"/> event for the new one - both in the same operation.
/// </remarks>
/// <param name="TaskId">ID of the task to assign.</param>
/// <param name="AssigneeId">ID of the user to assign the task to.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
/// <param name="IfMatch">
/// The task's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record AssignTask(
    Guid TaskId,
    Guid AssigneeId,
    Guid RequestedByUserId,
    byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="AssignTask"/> by invoking <see cref="ProjectTask.Assign"/>.
internal sealed class AssignTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<AssignTask>
{
    public async Task HandleAsync(AssignTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        ConcurrencyGuard.EnsureMatch(task.RowVersion, command.IfMatch, "Task", command.TaskId);

        // Archived board/project = read-only tasks (see TaskContainerGuard).
        await TaskContainerGuard.EnsureWritableAsync(db, task.BoardId, cancellationToken);

        task.Assign(command.AssigneeId, command.RequestedByUserId, timeProvider.GetUtcNow());

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
/// Validates <see cref="AssignTask"/> commands.
/// </summary>
/// <remarks>
/// Verifies that the target assignee user exists. Project-membership
/// authorization (is this user actually a member of the project?) is a
/// policy concern enforced at the authorization layer - not here.
/// </remarks>
internal sealed class AssignTaskValidator : AbstractValidator<AssignTask>
{
    public AssignTaskValidator(IAppDbContext db)
    {
        RuleFor(t => t.TaskId)
            .NotEmpty();

        // The assignee must be able to work on the task: a contributor of the task's project
        // (authorization only checks the caller). Also rejects unknown and cross-tenant users.
        // An unknown task passes here so the handler can report 404.
        RuleFor(t => t.AssigneeId)
            .NotEmpty()
            .MustAsync(async (command, assigneeId, ct) =>
                await ProjectAccessQueries.ProjectOfTaskAsync(db, command.TaskId, ct) is not Guid projectId
                || await ProjectAccessQueries.CanContributeAsync(db, projectId, assigneeId, ct))
            .WithMessage("The assignee must be a Member or Admin of this task's project.");

        RuleFor(t => t.RequestedByUserId)
            .NotEmpty();

        RuleFor(t => t.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
