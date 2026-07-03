using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="DeleteTaskHandler"/> soft-deletes <see cref="ProjectTask"/>
/// and raises <see cref="TaskDeleted"/>.
/// </summary>
public class DeleteTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_SoftDeletesTaskAndRaisesTaskDeletedEvent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new DeleteTaskHandler(db, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new DeleteTask(TaskId: task.Id, RequestedByUserId: requestedBy),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.True(reloaded.IsDeleted);
        Assert.Equal(Now, reloaded.DeletedAt);

        TaskDeleted evt = Assert.Single(reloaded.DomainEvents.OfType<TaskDeleted>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(requestedBy, evt.DeletedByUserId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new DeleteTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new DeleteTask(TaskId: Guid.CreateVersion7(), RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }
}
