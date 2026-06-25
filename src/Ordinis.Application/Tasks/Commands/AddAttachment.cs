using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Records a new file attachment on a task.
/// </summary>
/// <remarks>
/// This command records metadata about an already-uploaded file.
/// The file itself is expected to have been uploaded to blob storage before
/// this command is issued; the <paramref name="DownloadUrl"/> is the
/// pre-signed or permanent URL returned by the storage provider.
/// </remarks>
/// <param name="TaskId">ID of the task to attach the file to.</param>
/// <param name="FileName">Original file name (e.g. <c>spec.pdf</c>).</param>
/// <param name="ContentType">MIME type (e.g. <c>application/pdf</c>).</param>
/// <param name="SizeInBytes">File size in bytes. Must be greater than zero.</param>
/// <param name="DownloadUrl">URL from which the file can be retrieved.</param>
/// <param name="UploadedByUserId">ID of the user uploading the attachment.</param>
public sealed record AddAttachment(
    Guid TaskId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    string DownloadUrl,
    Guid UploadedByUserId) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="AddAttachment"/> by invoking <see cref="ProjectTask.AddAttachment"/>.
/// and returning the new attachment's ID.
/// </summary>
internal sealed class AddAttachmentHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<AddAttachment, Guid>
{
    public async Task<Guid> HandleAsync(AddAttachment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        Attachment attachment = task.AddAttachment(
            fileName: command.FileName,
            contentType: command.ContentType,
            sizeInBytes: command.SizeInBytes,
            storageUrl: command.DownloadUrl,
            uploadedByUserId: command.UploadedByUserId,
            now: timeProvider.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);

        return attachment.Id;
    }
}

// Validator
/// <summary>
/// Validates <see cref="AddAttachment"/> commands.
/// </summary>
internal sealed class AddAttachmentValidator : AbstractValidator<AddAttachment>
{
    public AddAttachmentValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.SizeInBytes)
            .GreaterThan(0)
            .WithMessage("SizeInBytes must be greater than zero.");

        RuleFor(x => x.DownloadUrl)
            .NotEmpty()
            .MaximumLength(2048);

        RuleFor(x => x.UploadedByUserId)
            .NotEmpty();
    }
}
