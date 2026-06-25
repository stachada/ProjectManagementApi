using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Soft-deletes a task, making it invisible to all queries that use the
/// global EF Core query filter.
/// </summary>
/// <remarks>
/// No validator is registered for this command - it carries only an ID and a
/// user reference, both of which are non-empty GUIDs guaranteed by the route
/// binding and authentication middleware before this command is constructed.
/// Task existence is checked in the handler; a missing task returns 404.
/// </remarks>
/// <param name="TaskId">ID of the task to soft-delete.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
public sealed record DeleteTask(
    Guid TaskId,
    Guid RequestedByUserId
) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="DeleteTask"/> by soft-deleting the task via
/// <see cref="AuditableEntity.SoftDelete(DateTime)"/>
/// </summary>
/// <remarks>
/// <para>
/// Soft delete sets <c>IsDeleted = true</c> and <c>DeletedAt = now</c> on the entity.
/// The global EF Core query filter on <c>ProjectTask</c> automatically
/// excludes soft-deleted records form all subsequent queries.
/// </para>
/// <para>
/// Child comments and attachments are not independently soft-deleted - they become
/// unreachable because the task itself is filtered out. If the domain later requires
/// cascading soft-delete on children, that logic belongs in the aggregate or
/// a dedicated domain service, not here.
/// </para>
/// </remarks>
/// <param name="db"></param>
/// <param name="timeProvider"></param>
internal sealed class DeleteTaskHandler(
    IAppDbContext db,
    TimeProvider timeProvider
) : ICommandHandler<DeleteTask>
{
    public async Task HandleAsync(DeleteTask command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        task.SoftDelete(timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
    }
}
