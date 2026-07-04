using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Application.Users.Queries;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Queries;

/// <summary>
/// Verifies <see cref="GetUserTasksHandler"/> returns paged tasks scoped to the
/// user, applies optional filters, and throws when the user does not exist.
/// </summary>
public class GetUserTasksHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsTasksAssignedToUser()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(displayName: "Alice");
        db.Users.Add(user);

        var boardId = Guid.CreateVersion7();
        ProjectTask mine = TaskBuilder.Create(boardId: boardId, now: Now);
        mine.Assign(user.Id, Guid.CreateVersion7(), Now);

        ProjectTask other = TaskBuilder.Create(boardId: boardId, now: Now);

        db.Tasks.AddRange(mine, other);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db)
            .HandleAsync(new GetUserTasks(user.Id), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(mine.Id, result.Items.Single().Id);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetUserTasksHandler(db)
                .HandleAsync(new GetUserTasks(Guid.CreateVersion7()), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_StatusFilter_ReturnsOnlyMatchingTasks()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);

        var boardId = Guid.CreateVersion7();
        ProjectTask todo = TaskBuilder.Create(boardId: boardId, now: Now);
        todo.Assign(user.Id, Guid.CreateVersion7(), Now);

        ProjectTask inProgress = TaskBuilder.Create(boardId: boardId, now: Now);
        inProgress.Assign(user.Id, Guid.CreateVersion7(), Now);
        // Backlog → ToDo → InProgress (state machine does not allow Backlog → InProgress directly)
        inProgress.Move(ProjectTaskStatus.ToDo, Guid.CreateVersion7(), Now);
        inProgress.Move(ProjectTaskStatus.InProgress, Guid.CreateVersion7(), Now);

        db.Tasks.AddRange(todo, inProgress);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db)
            .HandleAsync(
                new GetUserTasks(user.Id, new TaskFilter { Status = ProjectTaskStatus.InProgress }),
                CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(inProgress.Id, result.Items.Single().Id);
    }

    [Fact]
    public async Task HandleAsync_Pagination_ReturnsCorrectPage()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);

        var boardId = Guid.CreateVersion7();
        for (int i = 0; i < 5; i++)
        {
            ProjectTask t = TaskBuilder.Create(boardId: boardId, title: $"Task {i}", now: Now);
            t.Assign(user.Id, Guid.CreateVersion7(), Now);
            db.Tasks.Add(t);
        }
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db)
            .HandleAsync(
                new GetUserTasks(user.Id, new TaskFilter { Page = 2, PageSize = 2 }),
                CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Page);
    }

    [Fact]
    public async Task HandleAsync_AssigneeDisplayNameResolvedFromUserTable()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(displayName: "Bob");
        db.Users.Add(user);

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(user.Id, Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db)
            .HandleAsync(new GetUserTasks(user.Id), CancellationToken.None);

        Assert.Equal("Bob", result.Items.Single().AssigneeDisplayName);
    }
}
