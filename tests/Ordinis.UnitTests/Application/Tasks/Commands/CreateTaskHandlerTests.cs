using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="CreateTaskHandler"/> creates a <see cref="ProjectTask"/> with the
/// submitted fields, raises <see cref="TaskCreated"/>, and returns the new task's ID.
/// </summary>
public class CreateTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsTaskWithSubmittedFieldsAndRaisesTaskCreated()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateTaskHandler(db, new FakeTimeProvider(Now));
        var boardId = Guid.CreateVersion7();
        var reporterId = Guid.CreateVersion7();
        DateTimeOffset dueDate = Now.AddDays(7);

        Guid taskId = await handler.HandleAsync(
            new CreateTask(boardId, "Fix login redirect bug", "Investigate the OAuth callback", Priority.High, null, dueDate, reporterId),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == taskId);
        Assert.Equal(boardId, reloaded.BoardId);
        Assert.Equal(reporterId, reloaded.ReporterId);
        Assert.Equal("Fix login redirect bug", reloaded.Title);
        Assert.Equal("Investigate the OAuth callback", reloaded.Description);
        Assert.Equal(Priority.High, reloaded.Priority);
        Assert.Equal(ProjectTaskStatus.Backlog, reloaded.Status);
        Assert.Equal(dueDate, reloaded.DueDate);
        Assert.Null(reloaded.AssigneeId);

        TaskCreated raised = Assert.IsType<TaskCreated>(Assert.Single(reloaded.DomainEvents));
        Assert.Equal(taskId, raised.TaskId);
        Assert.Equal(boardId, raised.BoardId);
        Assert.Equal(reporterId, raised.ReporterId);
        Assert.Equal("Fix login redirect bug", raised.Title);
        Assert.Equal(Now, raised.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_WithAssigneeId_AssignsImmediatelyAndRaisesTaskCreatedThenTaskAssigned()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateTaskHandler(db, new FakeTimeProvider(Now));
        var reporterId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();

        Guid taskId = await handler.HandleAsync(
            new CreateTask(Guid.CreateVersion7(), "Triage backlog", null, Priority.Medium, assigneeId, null, reporterId),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == taskId);
        Assert.Equal(assigneeId, reloaded.AssigneeId);

        Assert.Collection(reloaded.DomainEvents,
            evt => Assert.IsType<TaskCreated>(evt),
            evt =>
            {
                TaskAssigned taskAssigned = Assert.IsType<TaskAssigned>(evt);
                Assert.Equal(assigneeId, taskAssigned.AssigneeId);
                Assert.Equal(reporterId, taskAssigned.AssignedByUserId);
            });
    }

    [Fact]
    public async Task HandleAsync_WithoutDueDate_PersistsNullDueDate()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateTaskHandler(db, new FakeTimeProvider(Now));

        Guid taskId = await handler.HandleAsync(
            new CreateTask(Guid.CreateVersion7(), "No deadline yet", null, Priority.Low, null, null, Guid.CreateVersion7()),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == taskId);
        Assert.Null(reloaded.DueDate);
    }
}
