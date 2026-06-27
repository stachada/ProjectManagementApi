using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
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
        using TestAppDbContext db = TestAppDbContext.CreateInMemory();
        ProjectTask task = TaskBuilder.Create(now: Now);
        Attachment attachment = task.AddAttachment(
            "report.pdf", "application/pdf", 12_345, "https://storage.test/files/report.pdf", Guid.NewGuid(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));
        var removerId = Guid.NewGuid();

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
        using TestAppDbContext db = TestAppDbContext.CreateInMemory();
        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new RemoveAttachment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                CancellationToken.None));

        Assert.Null(fileStorage.DeletedDownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_UnknownAttachmentId_ThrowsNotFoundExceptionAndDoesNotDelete()
    {
        using TestAppDbContext db = TestAppDbContext.CreateInMemory();
        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var fileStorage = new FakeFileStorageService();
        var handler = new RemoveAttachmentHandler(db, fileStorage, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new RemoveAttachment(task.Id, Guid.NewGuid(), Guid.NewGuid()),
                CancellationToken.None));

        Assert.Null(fileStorage.DeletedDownloadUrl);
    }
}
