using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Application.Users.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Queries;

/// <summary>
/// Verifies <see cref="GetUserTasksHandler"/> returns paged tasks scoped to the
/// user, applies optional filters, restricts results to projects the caller can see,
/// and throws when the user does not exist.
/// </summary>
public class GetUserTasksHandlerTests
{
    private static readonly Guid TestOrganizationId = Guid.CreateVersion7();
    private static readonly ICurrentUserService AdminCurrentUser = new FakeCurrentUserService(
        userId: Guid.CreateVersion7(),
        organizationId: TestOrganizationId,
        isOrgAdmin: true);

    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsTasksAssignedToUser()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(displayName: "Alice");
        db.Users.Add(user);

        Guid boardId = SeedBoard(db);
        ProjectTask mine = TaskBuilder.Create(boardId: boardId, now: Now);
        mine.Assign(user.Id, Guid.CreateVersion7(), Now);

        ProjectTask other = TaskBuilder.Create(boardId: boardId, now: Now);

        db.Tasks.AddRange(mine, other);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, AdminCurrentUser)
            .HandleAsync(new GetUserTasks(user.Id), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(mine.Id, result.Items.Single().Id);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetUserTasksHandler(db, AdminCurrentUser)
                .HandleAsync(new GetUserTasks(Guid.CreateVersion7()), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_StatusFilter_ReturnsOnlyMatchingTasks()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);

        Guid boardId = SeedBoard(db);
        ProjectTask todo = TaskBuilder.Create(boardId: boardId, now: Now);
        todo.Assign(user.Id, Guid.CreateVersion7(), Now);

        ProjectTask inProgress = TaskBuilder.Create(boardId: boardId, now: Now);
        inProgress.Assign(user.Id, Guid.CreateVersion7(), Now);
        // Backlog → ToDo → InProgress (state machine does not allow Backlog → InProgress directly)
        inProgress.Move(ProjectTaskStatus.ToDo, Guid.CreateVersion7(), Now);
        inProgress.Move(ProjectTaskStatus.InProgress, Guid.CreateVersion7(), Now);

        db.Tasks.AddRange(todo, inProgress);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, AdminCurrentUser)
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

        Guid boardId = SeedBoard(db);
        for (int i = 0; i < 5; i++)
        {
            ProjectTask t = TaskBuilder.Create(boardId: boardId, title: $"Task {i}", now: Now);
            t.Assign(user.Id, Guid.CreateVersion7(), Now);
            db.Tasks.Add(t);
        }
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, AdminCurrentUser)
            .HandleAsync(
                new GetUserTasks(user.Id, new TaskFilter { Page = 2, PageSize = 2 }),
                CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Page);
    }

    /// <summary>
    /// An unknown sort field falls back to createdAt in the requested direction — the same rule
    /// as every other task list — rather than always newest first.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_UnknownSortField_SortsByCreatedAtInTheRequestedDirection(bool sortDescending)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);

        Guid boardId = SeedBoard(db);
        var created = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            ProjectTask task = TaskBuilder.Create(boardId: boardId, title: $"Task {i}", now: Now);
            task.Assign(user.Id, Guid.CreateVersion7(), Now);
            task.CreatedAt = Now.AddMinutes(i);
            db.Tasks.Add(task);
            created.Add(task.Id);
        }
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, AdminCurrentUser)
            .HandleAsync(
                new GetUserTasks(user.Id, new TaskFilter { SortBy = "colour", SortDescending = sortDescending }),
                CancellationToken.None);

        IEnumerable<Guid> expected = sortDescending ? Enumerable.Reverse(created) : created;
        Assert.Equal(expected, result.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task HandleAsync_AssigneeDisplayNameResolvedFromUserTable()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(displayName: "Bob");
        db.Users.Add(user);

        ProjectTask task = TaskBuilder.Create(boardId: SeedBoard(db), now: Now);
        task.Assign(user.Id, Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, AdminCurrentUser)
            .HandleAsync(new GetUserTasks(user.Id), CancellationToken.None);

        Assert.Equal("Bob", result.Items.Single().AssigneeDisplayName);
    }

    [Fact]
    public async Task HandleAsync_NonAdminCaller_ExcludesTasksInProjectsCallerIsNotMemberOf()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User assignee = UserBuilder.Create(displayName: "Carol");
        db.Users.Add(assignee);

        var callerId = Guid.CreateVersion7();

        // The caller created (and so is an Admin member of) the first project only.
        Project shared = ProjectBuilder.Create(organizationId: TestOrganizationId, createdByUserId: callerId);
        Project hidden = ProjectBuilder.Create(organizationId: TestOrganizationId);
        Board sharedBoard = BoardBuilder.Create(projectId: shared.Id);
        Board hiddenBoard = BoardBuilder.Create(projectId: hidden.Id);
        db.Projects.AddRange(shared, hidden);
        db.Boards.AddRange(sharedBoard, hiddenBoard);

        ProjectTask visible = TaskBuilder.Create(boardId: sharedBoard.Id, now: Now);
        visible.Assign(assignee.Id, Guid.CreateVersion7(), Now);
        ProjectTask invisible = TaskBuilder.Create(boardId: hiddenBoard.Id, now: Now);
        invisible.Assign(assignee.Id, Guid.CreateVersion7(), Now);
        db.Tasks.AddRange(visible, invisible);
        await db.SaveChangesAsync();

        var caller = new FakeCurrentUserService(callerId, TestOrganizationId, isOrgAdmin: false);

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, caller)
            .HandleAsync(new GetUserTasks(assignee.Id), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(visible.Id, result.Items.Single().Id);
    }

    /// <summary>
    /// Seeds a project in <see cref="TestOrganizationId"/> with one board - tasks must sit on a
    /// real board in a real project for the handler's caller-visibility filter to see them.
    /// </summary>
    [Fact]
    public async Task HandleAsync_TaskWithCommentsAndAttachments_ReportsLiveCountsAndAssigneeName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(displayName: "Alice");
        db.Users.Add(user);
        Guid boardId = SeedBoard(db);
        ProjectTask task = TaskBuilder.Create(boardId: boardId, now: Now).WithCountableChildren();
        task.Assign(user.Id, Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); // a real request starts untracked - see WithCountableChildren

        PagedResult<TaskSummaryDto> result = await new GetUserTasksHandler(db, AdminCurrentUser)
            .HandleAsync(new GetUserTasks(user.Id), CancellationToken.None);

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal(TaskBuilder.ExpectedCommentCount, only.CommentCount);
        Assert.Equal(TaskBuilder.ExpectedAttachmentCount, only.AttachmentCount);
        Assert.Equal("Alice", only.AssigneeDisplayName);
    }

    private static Guid SeedBoard(TestAppDbContext db)
    {
        Project project = ProjectBuilder.Create(organizationId: TestOrganizationId);
        Board board = BoardBuilder.Create(projectId: project.Id);
        db.Projects.Add(project);
        db.Boards.Add(board);
        return board.Id;
    }
}
