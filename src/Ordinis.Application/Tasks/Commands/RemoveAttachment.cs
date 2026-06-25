using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Removes an attachment from a task.
/// </summary>
/// <remarks>
/// Attachments used hard removal from the aggregate's collection - they have no
/// soft-delete mechanism (<see cref="Attachment"/> inherits from <see cref="Entity"/>,
/// not <see cref="AuditableEntity"/>). The <see cref="AttachmentRemoved"/> domain event
/// signals downstream consumers (e.g. blob storage cleanup) via the Outbox.
/// </remarks>
/// <param name="TaskId">ID of the task that owns the attachment.</param>
/// <param name="AttachmentId">ID of the attachment to remove.</param>
/// <param name="RemovedByUserId">ID of the user issuing the command.</param>
public sealed record RemoveAttachment(
    Guid TaskId,
    Guid AttachmentId,
    Guid RemovedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RemoveAttachment"/> by invoking <see cref="ProjectTask.RemoveAttachment"/>.
/// </summary>
internal sealed class RemoveAttachmentHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<RemoveAttachment>
{
    public async Task HandleAsync(RemoveAttachment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        task.RemoveAttachment(
            attachmentId: command.AttachmentId,
            removedByUserId: command.RemovedByUserId,
            now: timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
    }
}
