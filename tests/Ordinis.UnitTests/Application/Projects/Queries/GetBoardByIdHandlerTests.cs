using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetBoardByIdHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsCorrectBoardDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create(name: "Sprint 1");
        db.Boards.Add(board);
        db.Tasks.Add(TaskBuilder.Create(boardId: board.Id));
        db.Tasks.Add(TaskBuilder.Create(boardId: board.Id));
        await db.SaveChangesAsync();

        BoardDto dto = await new GetBoardByIdHandler(db)
            .HandleAsync(new GetBoardById(board.Id));

        Assert.Equal(board.Id, dto.Id);
        Assert.Equal("Sprint 1", dto.Name);
        Assert.Equal(board.ProjectId, dto.ProjectId);
        Assert.Equal(2, dto.TaskCount);
        Assert.Equal(2, dto.Tasks.Count);
        Assert.False(dto.TasksAreTruncated);
    }

    [Fact]
    public async Task HandleAsync_NonExistentBoard_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetBoardByIdHandler(db)
                .HandleAsync(new GetBoardById(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_TaskCountReflectsTotalNotJustEmbedded()
    {
        // TaskCount is a separate COUNT query — verify it reflects the true total
        // even when there are no tasks (the embedded list and total both = 0).
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        BoardDto dto = await new GetBoardByIdHandler(db)
            .HandleAsync(new GetBoardById(board.Id));

        Assert.Equal(0, dto.TaskCount);
        Assert.Empty(dto.Tasks);
        Assert.False(dto.TasksAreTruncated);
    }
}
