using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;

namespace Ordinis.Application.Tasks.Commands;

// Command
/// <summary>
/// Uploads a file and records it as a new attachment on a task.
/// </summary>
/// <remarks>
/// The handler uploads <paramref name="FileStream"/> to blob storage via
/// <see cref="IFileStorageService"/> before recording the attachment — callers never
/// resolve a storage URL themselves.
/// </remarks>
/// <param name="TaskId">ID of the task to attach the file to.</param>
/// <param name="FileName">Original file name (e.g. <c>spec.pdf</c>).</param>
/// <param name="ContentType">MIME type (e.g. <c>application/pdf</c>).</param>
/// <param name="SizeInBytes">File size in bytes. Must be greater than zero.</param>
/// <param name="FileStream">The file content to upload.</param>
/// <param name="UploadedByUserId">ID of the user uploading the attachment.</param>
public sealed record AddAttachment(
    Guid TaskId,
    string FileName,
    string ContentType,
    long SizeInBytes,
    Stream FileStream,
    Guid UploadedByUserId) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="AddAttachment"/> by uploading the file via <see cref="IFileStorageService"/>
/// and invoking <see cref="ProjectTask.AddAttachment"/> with the resulting storage URL.
/// </summary>
internal sealed class AddAttachmentHandler(
    IAppDbContext db,
    IFileStorageService fileStorage,
    TimeProvider timeProvider) : ICommandHandler<AddAttachment, Guid>
{
    public async Task<Guid> HandleAsync(AddAttachment command, CancellationToken cancellationToken)
    {
        ProjectTask task = await db.Tasks
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
                ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

        string downloadUrl = await fileStorage.UploadAsync(
            command.FileStream,
            command.FileName,
            command.ContentType,
            cancellationToken);

        Attachment attachment = task.AddAttachment(
            fileName: command.FileName,
            contentType: command.ContentType,
            sizeInBytes: command.SizeInBytes,
            storageUrl: downloadUrl,
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

        RuleFor(x => x.FileStream)
            .NotNull();

        RuleFor(x => x.UploadedByUserId)
            .NotEmpty();
    }
}
