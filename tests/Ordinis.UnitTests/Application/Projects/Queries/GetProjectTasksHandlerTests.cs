using Ordinis.Application.Common;
using Ordinis.Application.Projects.Queries;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetProjectTasksHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsOnlyTasksScopedToProject()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        Project projectA = ProjectBuilder.Create();
        Project projectB = ProjectBuilder.Create();
        db.Projects.AddRange(projectA, projectB);

        Board boardA = BoardBuilder.Create(projectId: projectA.Id);
        Board boardB = BoardBuilder.Create(projectId: projectB.Id);
        db.Boards.AddRange(boardA, boardB);

        db.Tasks.Add(TaskBuilder.Create(boardId: boardA.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: boardA.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: boardB.Id));
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetProjectTasksHandler(db)
            .HandleAsync(new GetProjectTasks(projectA.Id));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(boardA.Id, t.BoardId));
    }

    [Fact]
    public async Task HandleAsync_NonExistentProject_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetProjectTasksHandler(db)
                .HandleAsync(new GetProjectTasks(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_StatusFilter_FiltersWithinProjectScope()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        Board board = BoardBuilder.Create(projectId: project.Id);
        db.Boards.Add(board);

        var backlogTask = TaskBuilder.Create(boardId: board.Id, title: "Backlog");
        var cancelledTask = TaskBuilder.Create(boardId: board.Id, title: "Cancelled");
        cancelledTask.Move(ProjectTaskStatus.Cancelled, Guid.CreateVersion7(),
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        db.Tasks.AddRange(backlogTask, cancelledTask);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetProjectTasksHandler(db)
            .HandleAsync(new GetProjectTasks(project.Id,
                new TaskFilter(Status: ProjectTaskStatus.Cancelled)));

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal("Cancelled", only.Title);
    }

    [Fact]
    public async Task HandleAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        Board board = BoardBuilder.Create(projectId: project.Id);
        db.Boards.Add(board);
        db.Tasks.AddRange(
            TaskBuilder.Create(boardId: board.Id),
            TaskBuilder.Create(boardId: board.Id),
            TaskBuilder.Create(boardId: board.Id));
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetProjectTasksHandler(db)
            .HandleAsync(new GetProjectTasks(project.Id,
                new TaskFilter(Page: 2, PageSize: 1)));

        Assert.Equal(3, result.TotalCount);
        Assert.Single(result.Items);
    }
}
