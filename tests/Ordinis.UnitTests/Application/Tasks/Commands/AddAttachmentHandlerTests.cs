using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="AddAttachmentHandler"/> uploads via <see cref="IFileStorageService"/>
/// before recording the attachment.
/// </summary>
public class AddAttachmentHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_UploadsFileAndPersistsAttachmentWithReturnedUrl()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var fileStorage = new FakeFileStorageService
        {
            DownloadUrlToReturn = "https://storage.test/files/report.pdf"
        };
        var handler = new AddAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));
        var uploaderId = Guid.CreateVersion7();

        using var content = new MemoryStream();
        Guid attachmentId = await handler.HandleAsync(
            new AddAttachment(task.Id, "report.pdf", "application/pdf", 12_345, content, uploaderId),
            CancellationToken.None);

        Assert.Equal("report.pdf", fileStorage.UploadedFileName);

        ProjectTask reloaded = await db.Tasks
            .Include(t => t.Attachments)
            .SingleAsync(t => t.Id == task.Id);
        Attachment attachment = Assert.Single(reloaded.Attachments);
        Assert.Equal(attachmentId, attachment.Id);
        Assert.Equal("https://storage.test/files/report.pdf", attachment.StorageUrl);
        Assert.Equal(uploaderId, attachment.UploadedByUserId);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundExceptionAndDoesNotUpload()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var fileStorage = new FakeFileStorageService();
        var handler = new AddAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        using var content = new MemoryStream();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new AddAttachment(Guid.CreateVersion7(), "x.pdf", "application/pdf", 1, content, Guid.CreateVersion7()),
                CancellationToken.None));

        Assert.Null(fileStorage.UploadedFileName);
    }
}
