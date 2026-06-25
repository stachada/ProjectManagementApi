using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Soft-deletes a comment from a task.
/// </summary>
/// <param name="TaskId">The ID of the task that owns the comment.</param>
/// <param name="CommentId">The ID of the comment to remove.</param>
/// <param name="RequestedByUserId">ID of the user issuing this command.</param>
public sealed record RemoveComment(
    Guid TaskId,
    Guid CommentId,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RemoveComment"/> by invoking <see cref="ProjectTask.RemoveComment"/>.
/// </summary>
/// <remarks>
/// No validator is needed - the handler's <see cref="NotFoundException"/> provides
/// sufficient guard for missing tasks and comments.
/// Authorization (only the author or an admin may delete) is enforced by
/// the policy layer.
/// </remarks>
internal sealed class RemoveCommentHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<RemoveComment>
{
    public async Task HandleAsync(RemoveComment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        task.RemoveComment(command.CommentId, command.RequestedByUserId, timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
    }
}
