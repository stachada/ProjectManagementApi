using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Removes the current assignee from a task.
/// </summary>
/// <remarks>
/// If the task is already unassigned, <see cref="ProjectTask.Unassign"/> is a
/// no-op at the domain level - no event is raised and the save is a no-change
/// round-trip. This is intentional: idempotent commands are easier to retry safely.
/// </remarks>
/// <param name="TaskId">ID of the task to unassign.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
public sealed record UnassignTask(
    Guid TaskId,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UnassignTask"/> by invoking <see cref="ProjectTask.Unassign"/>.
/// </summary>
/// <remarks>
/// No validator is registered for this command - it carries only IDs that are
/// guaranteed non-empty by route binding and authentication middleware.
/// </remarks>
internal sealed class UnassignTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<UnassignTask>
{
    public async Task HandleAsync(UnassignTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

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
