using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Removes an attachment from a task.
/// </summary>
/// <remarks>
/// Attachments use hard removal from the aggregate's collection - they have no
/// soft-delete mechanism (<see cref="Attachment"/> inherits from <see cref="Entity"/>,
/// not <see cref="AuditableEntity"/>). The handler deletes the file from blob storage
/// via <see cref="IFileStorageService"/> only after the database save succeeds —
/// orphaned files on disk are recoverable; orphaned DB rows pointing at missing files
/// are not.
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
/// Handles <see cref="RemoveAttachment"/> by invoking <see cref="ProjectTask.RemoveAttachment"/>
/// and then deleting the file from storage via <see cref="IFileStorageService"/>.
/// </summary>
internal sealed class RemoveAttachmentHandler(
    IAppDbContext db,
    IFileStorageService fileStorage,
    TimeProvider timeProvider) : ICommandHandler<RemoveAttachment>
{
    public async Task HandleAsync(RemoveAttachment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        Attachment attachment = task.Attachments.FirstOrDefault(a => a.Id == command.AttachmentId)
            ?? throw new NotFoundException(nameof(Attachment), command.AttachmentId);
        string storageUrl = attachment.StorageUrl;

        task.RemoveAttachment(
            attachmentId: command.AttachmentId,
            removedByUserId: command.RemovedByUserId,
            now: timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);

        await fileStorage.DeleteAsync(storageUrl, cancellationToken);
    }
}
