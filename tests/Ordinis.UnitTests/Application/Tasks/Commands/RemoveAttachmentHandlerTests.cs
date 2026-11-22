using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="RemoveAttachmentHandler"/> deletes from storage via
/// <see cref="IFileStorageService"/> only after the database save succeeds.
/// </summary>
public class RemoveAttachmentHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ExistingAttachment_RemovesFromDbAndDeletesFromStorageAfterSave()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        var removerId = Guid.CreateVersion7(); // the uploader may always remove their own attachment
        Attachment attachment = task.AddAttachment(
            "report.pdf", "application/pdf", 12_345, "https://storage.test/files/report.pdf", removerId, Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new RemoveAttachment(task.Id, attachment.Id, removerId),
            CancellationToken.None);

        Assert.Equal("https://storage.test/files/report.pdf", fileStorage.DeletedDownloadUrl);

        ProjectTask reloaded = await db.Tasks
            .Include(t => t.Attachments)
            .SingleAsync(t => t.Id == task.Id);
        Assert.Empty(reloaded.Attachments);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundExceptionAndDoesNotDelete()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new RemoveAttachment(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()),
                CancellationToken.None));

        Assert.Null(fileStorage.DeletedDownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_UnknownAttachmentId_ThrowsNotFoundExceptionAndDoesNotDelete()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new RemoveAttachment(task.Id, Guid.CreateVersion7(), Guid.CreateVersion7()),
                CancellationToken.None));

        Assert.Null(fileStorage.DeletedDownloadUrl);
    }

    // GAP-11: someone who neither uploaded the attachment nor moderates the project cannot remove
    // it — and the stored file must survive the rejected request.
    [Fact]
    public async Task HandleAsync_RemoverIsNeitherUploaderNorModerator_ThrowsAndKeepsFile()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        Attachment attachment = task.AddAttachment(
            "report.pdf", "application/pdf", 12_345, "https://storage.test/files/report.pdf", Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new RemoveAttachment(task.Id, attachment.Id, Guid.CreateVersion7()),
                CancellationToken.None));

        Assert.Equal("task.attachment-not-uploader", ex.ErrorCode);
        Assert.Null(fileStorage.DeletedDownloadUrl);
        Assert.Single((await db.Tasks.Include(t => t.Attachments).SingleAsync(t => t.Id == task.Id)).Attachments);
    }
}
