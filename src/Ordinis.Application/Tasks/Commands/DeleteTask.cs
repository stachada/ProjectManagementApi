using FluentValidation;
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
/// Task existence is checked in the handler; a missing task returns 404.
/// </remarks>
/// <param name="TaskId">ID of the task to soft-delete.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
/// <param name="IfMatch">
/// The task's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record DeleteTask(
    Guid TaskId,
    Guid RequestedByUserId,
    byte[]? IfMatch
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

        ConcurrencyGuard.EnsureMatch(task.RowVersion, command.IfMatch, "Task", command.TaskId);

        DateTimeOffset now = timeProvider.GetUtcNow();

        task.Delete(command.RequestedByUserId, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(nameof(ProjectTask), command.TaskId, ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="DeleteTask"/> commands.
/// </summary>
/// <remarks>
/// Only the <see cref="DeleteTask.IfMatch"/> concurrency token is validated here - the IDs
/// are guaranteed non-empty by route binding and authentication middleware, matching this
/// command's original no-validator design.
/// </remarks>
internal sealed class DeleteTaskValidator : AbstractValidator<DeleteTask>
{
    public DeleteTaskValidator()
    {
        RuleFor(t => t.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
