using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="UnassignTaskHandler"/> removes the assignee from <see cref="ProjectTask"/>
/// and raises <see cref="TaskUnassigned"/>, and correctly surfaces domain and concurrency errors.
/// </summary>
public class UnassignTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_UnassignsTaskAndRaisesTaskUnassignedEvent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();
        var assignee = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(assignee, requestedBy, Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new UnassignTaskHandler(db, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new UnassignTask(TaskId: task.Id, RequestedByUserId: requestedBy, IfMatch: task.RowVersion),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Null(reloaded.AssigneeId);

        TaskUnassigned evt = Assert.Single(reloaded.DomainEvents.OfType<TaskUnassigned>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(requestedBy, evt.UnassignedByUserId);
        Assert.Equal(assignee, evt.PreviousAssigneeId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new UnassignTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new UnassignTask(TaskId: Guid.CreateVersion7(), RequestedByUserId: Guid.CreateVersion7(), IfMatch: null),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TaskWithoutAssignee_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new UnassignTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new UnassignTask(TaskId: task.Id, RequestedByUserId: requestedBy, IfMatch: task.RowVersion),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Simulate a concurrent write between this context's load and save by forging
        // a stale OriginalValue — same mismatch EF Core would detect against a real DB.
        db.Entry(task).Property(t => t.RowVersion).OriginalValue = [1, 2, 3, 4, 5, 6, 7, 8];

        var handler = new UnassignTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            handler.HandleAsync(
                new UnassignTask(TaskId: task.Id, RequestedByUserId: Guid.CreateVersion7(), IfMatch: null),
                CancellationToken.None));
    }
}
