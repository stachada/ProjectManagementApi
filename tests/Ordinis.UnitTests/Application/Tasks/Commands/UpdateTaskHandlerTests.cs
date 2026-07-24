using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="UpdateTaskHandler"/> updates <see cref="ProjectTask"/> with the
/// submitted fields, raises <see cref="TaskUpdated"/>, and returns the updated task's ID.
/// </summary>
public class UpdateTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesExistingTaskWithSubmittedFields()
    {
        // Setup
        using TestAppDbContext db = TestDbContextFactory.Create();

        // Create a task to update
        var existing = ProjectTask.Create(
            boardId: Guid.CreateVersion7(),
            reporterId: Guid.CreateVersion7(),
            title: "Original title",
            now: Now,
            description: "Original description",
            priority: Priority.Medium,
            dueDate: Now.AddDays(5));

        // Add the task to the database
        db.Tasks.Add(existing);
        await db.SaveChangesAsync(CancellationToken.None);

        // Create handler
        var handler = new UpdateTaskHandler(db, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new UpdateTask(
                TaskId: existing.Id,
                Title: "Updated title",
                Description: "Updated description",
                Priority: Priority.High,
                DueDate: Now.AddDays(3),
                RequestedByUserId: Guid.CreateVersion7(),
                IfMatch: existing.RowVersion),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == existing.Id);

        Assert.Equal("Updated title", reloaded.Title);
        Assert.Equal("Updated description", reloaded.Description);
        Assert.Equal(Priority.High, reloaded.Priority);
        Assert.Equal(Now.AddDays(3), reloaded.DueDate);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_RaisesTaskUpdatedEventDescribingTheChangedFields()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        // Default task: Title = "Default Task Title", Priority = Medium, DueDate = Now.AddDays(7)
        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new UpdateTaskHandler(db, new FakeTimeProvider(Now));
        var requestedBy = Guid.CreateVersion7();

        await handler.HandleAsync(
            new UpdateTask(task.Id, "Updated Title", null, Priority.Medium, Now.AddDays(7), requestedBy, task.RowVersion),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        TaskUpdated evt = Assert.Single(reloaded.DomainEvents.OfType<TaskUpdated>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(requestedBy, evt.UpdatedByUserId);
        Assert.Equal(Now, evt.OccurredAt);

        // Priority and DueDate were resubmitted unchanged, so only Title appears in Changes.
        KeyValuePair<string, (object? Before, object? After)> onlyChange = Assert.Single(evt.Changes);
        Assert.Equal("Title", onlyChange.Key);
        Assert.Equal("Default Task Title", onlyChange.Value.Before);
        Assert.Equal("Updated Title", onlyChange.Value.After);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new UpdateTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new UpdateTask(Guid.CreateVersion7(), "Title", null, Priority.Medium, null, Guid.CreateVersion7(), null),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Simulate another request having already saved a conflicting change between this
        // context's load and its save, by making the tracked RowVersion's original value
        // stale relative to what's in the store - the same mismatch EF Core would detect
        // if a second context had updated the row in between.
        db.Entry(task).Property(t => t.RowVersion).OriginalValue = [1, 2, 3, 4, 5, 6, 7, 8];

        var handler = new UpdateTaskHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            handler.HandleAsync(
                new UpdateTask(task.Id, "Lost the race", null, Priority.Medium, null, Guid.CreateVersion7(), null),
                CancellationToken.None));
    }
}
