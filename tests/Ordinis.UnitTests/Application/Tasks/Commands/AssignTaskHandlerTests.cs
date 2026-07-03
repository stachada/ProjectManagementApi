using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="AssignTaskHandler"/> assigns <see cref="ProjectTask"/> to a user
/// and raises <see cref="TaskAssigned"/>, and correctly surfaces domain and concurrency errors.
/// </summary>
public class AssignTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_AssignsTaskAndRaisesTaskAssignedEvent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();
        var assignee = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new AssignTaskHandler(db, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new AssignTask(TaskId: task.Id, AssigneeId: assignee, RequestedByUserId: requestedBy),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(assignee, reloaded.AssigneeId);

        TaskAssigned evt = Assert.Single(reloaded.DomainEvents.OfType<TaskAssigned>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(assignee, evt.AssigneeId);
        Assert.Equal(requestedBy, evt.AssignedByUserId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new AssignTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new AssignTask(TaskId: Guid.CreateVersion7(), AssigneeId: Guid.CreateVersion7(), RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyAssigneeId_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new AssignTaskHandler(db, new FakeTimeProvider(Now));

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(
                new AssignTask(TaskId: task.Id, AssigneeId: Guid.Empty, RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AssigneeDuplicate_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var requestedBy = Guid.CreateVersion7();
        var assignee = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(assignee, requestedBy, Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new AssignTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AssignTask(TaskId: task.Id, AssigneeId: assignee, RequestedByUserId: requestedBy),
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

        var handler = new AssignTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            handler.HandleAsync(
                new AssignTask(TaskId: task.Id, AssigneeId: Guid.CreateVersion7(), RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }
}
