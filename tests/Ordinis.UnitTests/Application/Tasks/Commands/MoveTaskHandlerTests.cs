using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="MoveTaskHandler"/> transitions <see cref="ProjectTask"/> to a new status
/// and raises <see cref="TaskMoved"/> when the transition is valid, and throws a <see cref="DomainException"/>
/// when the transition is invalid.
/// </summary>
public class MoveTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_MovesTaskAndRaisesTaskMovedEvent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new MoveTaskHandler(db, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new MoveTask(TaskId: task.Id, NewStatus: ProjectTaskStatus.ToDo, RequestedByUserId: requestedBy, IfMatch: task.RowVersion),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(ProjectTaskStatus.ToDo, reloaded.Status);

        TaskMoved evt = Assert.Single(reloaded.DomainEvents.OfType<TaskMoved>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(ProjectTaskStatus.ToDo, evt.NewStatus);
        Assert.Equal(requestedBy, evt.MovedByUserId);
        Assert.Equal(Now, evt.OccurredAt);
        Assert.Equal(ProjectTaskStatus.Backlog, evt.PreviousStatus);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new MoveTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new MoveTask(TaskId: Guid.CreateVersion7(), NewStatus: ProjectTaskStatus.InProgress, RequestedByUserId: Guid.CreateVersion7(), IfMatch: null),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InvalidTransition_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new MoveTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new MoveTask(TaskId: task.Id, NewStatus: ProjectTaskStatus.Done, RequestedByUserId: requestedBy, IfMatch: task.RowVersion),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Simulate a concurrent write between this context's load and save by forging
        // a stale OriginalValue — same mismatch EF Core would detect against a real DB.
        db.Entry(task).Property(t => t.RowVersion).OriginalValue = [1, 2, 3, 4, 5, 6, 7, 8];

        var handler = new MoveTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            handler.HandleAsync(
                new MoveTask(TaskId: task.Id, NewStatus: ProjectTaskStatus.ToDo, RequestedByUserId: Guid.CreateVersion7(), IfMatch: null),
                CancellationToken.None));
    }
}
