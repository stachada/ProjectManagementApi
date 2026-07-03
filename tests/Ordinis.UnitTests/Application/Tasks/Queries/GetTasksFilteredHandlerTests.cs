using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Queries;

/// <summary>
/// Verifies <see cref="GetTasksFilteredHandler"/> returns correctly paginated, filtered,
/// and sorted <see cref="TaskSummaryDto"/> results with accurate <see cref="PagedResult{T}"/> metadata.
/// </summary>
public class GetTasksFilteredHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_NoFilter_ReturnsAllTasksWithCorrectTotalCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.AddRange(
            TaskBuilder.Create(now: Now),
            TaskBuilder.Create(now: Now),
            TaskBuilder.Create(now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(), CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_FilterByBoardId_ReturnsOnlyMatchingTasks()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var targetBoardId = Guid.CreateVersion7();
        db.Tasks.AddRange(
            TaskBuilder.Create(boardId: targetBoardId, now: Now),
            TaskBuilder.Create(now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(BoardId: targetBoardId)),
            CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(targetBoardId, only.BoardId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_FilterByAssigneeId_ReturnsOnlyMatchingTasks()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var assigneeId = Guid.CreateVersion7();
        ProjectTask assigned = TaskBuilder.Create(now: Now);
        assigned.Assign(assigneeId, Guid.CreateVersion7(), Now);
        db.Tasks.AddRange(assigned, TaskBuilder.Create(now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(AssigneeId: assigneeId)),
            CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(assigneeId, only.AssigneeId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_FilterByStatus_ReturnsOnlyMatchingTasks()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        ProjectTask inProgress = TaskBuilder.Create(now: Now);
        inProgress.Move(ProjectTaskStatus.ToDo, Guid.CreateVersion7(), Now);
        inProgress.Move(ProjectTaskStatus.InProgress, Guid.CreateVersion7(), Now);
        db.Tasks.AddRange(inProgress, TaskBuilder.Create(now: Now)); // second stays Backlog
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(Status: ProjectTaskStatus.InProgress)),
            CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(ProjectTaskStatus.InProgress, only.Status);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_FilterByPriority_ReturnsOnlyMatchingTasks()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.AddRange(
            TaskBuilder.Create(priority: Priority.High, now: Now),
            TaskBuilder.Create(priority: Priority.Low, now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(Priority: Priority.High)),
            CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(Priority.High, only.Priority);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_FilterByDueBefore_ReturnsOnlyTasksDueOnOrBefore()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.AddRange(
            TaskBuilder.Create(dueDate: Now.AddDays(3), now: Now),  // within range
            TaskBuilder.Create(dueDate: Now.AddDays(14), now: Now)); // outside range
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(DueBefore: Now.AddDays(7))),
            CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(Now.AddDays(3), only.DueDate);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_FilterByDueAfter_ReturnsOnlyTasksDueOnOrAfter()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.AddRange(
            TaskBuilder.Create(dueDate: Now.AddDays(3), now: Now),  // outside range
            TaskBuilder.Create(dueDate: Now.AddDays(14), now: Now)); // within range
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(DueAfter: Now.AddDays(7))),
            CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(Now.AddDays(14), only.DueDate);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_SecondPage_ReturnsCorrectItemsAndPreservesTotalCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        for (int i = 0; i < 5; i++)
        {
            db.Tasks.Add(TaskBuilder.Create(now: Now));
        }
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(Page: 2, PageSize: 3)),
            CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count); // 5 tasks − 3 on page 1 = 2 on page 2
        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.PageSize);
    }

    [Fact]
    public async Task HandleAsync_SortByTitleAscending_ReturnsTitlesAlphabetically()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.AddRange(
            TaskBuilder.Create(title: "Gamma", now: Now),
            TaskBuilder.Create(title: "Alpha", now: Now),
            TaskBuilder.Create(title: "Beta", now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(SortBy: "title", SortDescending: false)),
            CancellationToken.None);

        Assert.Equal(["Alpha", "Beta", "Gamma"], result.Items.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task HandleAsync_SortByTitleDescending_ReturnsTitlesReverseAlphabetically()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.AddRange(
            TaskBuilder.Create(title: "Gamma", now: Now),
            TaskBuilder.Create(title: "Alpha", now: Now),
            TaskBuilder.Create(title: "Beta", now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(SortBy: "title", SortDescending: true)),
            CancellationToken.None);

        Assert.Equal(["Gamma", "Beta", "Alpha"], result.Items.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task HandleAsync_PageSizeAboveMax_CapsPageSizeAt100()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Tasks.Add(TaskBuilder.Create(now: Now));
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(new TaskFilter(PageSize: 200)),
            CancellationToken.None);

        Assert.Equal(100, result.PageSize);
    }

    [Fact]
    public async Task HandleAsync_AssignedTask_ResolvesAssigneeDisplayNameInSummary()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var assignee = UserBuilder.Create(displayName: "Carol White");
        db.Users.Add(assignee);

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(assignee.Id, Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new GetTasksFilteredHandler(db);
        PagedResult<TaskSummaryDto> result = await handler.HandleAsync(
            new GetTasksFiltered(), CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(assignee.Id, only.AssigneeId);
        Assert.Equal("Carol White", only.AssigneeDisplayName);
    }
}
