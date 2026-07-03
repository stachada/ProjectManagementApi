using Ordinis.Application.Common;
using Ordinis.Application.Projects.Queries;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetBoardTasksHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsOnlyTasksForBoard()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board boardA = BoardBuilder.Create(name: "Board A");
        Board boardB = BoardBuilder.Create(name: "Board B");
        db.Boards.AddRange(boardA, boardB);
        db.Tasks.Add(TaskBuilder.Create(boardId: boardA.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: boardA.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: boardB.Id));
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetBoardTasksHandler(db)
            .HandleAsync(new GetBoardTasks(boardA.Id));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(boardA.Id, t.BoardId));
    }

    [Fact]
    public async Task HandleAsync_NonExistentBoard_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetBoardTasksHandler(db)
                .HandleAsync(new GetBoardTasks(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_StatusFilter_FiltersWithinBoardScope()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);

        var backlogTask = TaskBuilder.Create(boardId: board.Id, title: "Backlog");
        var cancelledTask = TaskBuilder.Create(boardId: board.Id, title: "Cancelled");
        cancelledTask.Move(ProjectTaskStatus.Cancelled, Guid.CreateVersion7(),
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        db.Tasks.AddRange(backlogTask, cancelledTask);
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetBoardTasksHandler(db)
            .HandleAsync(new GetBoardTasks(board.Id,
                new TaskFilter(Status: ProjectTaskStatus.Cancelled)));

        TaskSummaryDto only = Assert.Single(result.Items);
        Assert.Equal("Cancelled", only.Title);
    }

    [Fact]
    public async Task HandleAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        db.Tasks.AddRange(
            TaskBuilder.Create(boardId: board.Id),
            TaskBuilder.Create(boardId: board.Id),
            TaskBuilder.Create(boardId: board.Id));
        await db.SaveChangesAsync();

        PagedResult<TaskSummaryDto> result = await new GetBoardTasksHandler(db)
            .HandleAsync(new GetBoardTasks(board.Id,
                new TaskFilter(Page: 2, PageSize: 2)));

        Assert.Equal(3, result.TotalCount);
        Assert.Single(result.Items);
    }
}
