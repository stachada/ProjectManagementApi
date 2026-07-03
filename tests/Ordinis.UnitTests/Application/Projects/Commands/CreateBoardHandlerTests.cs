using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class CreateBoardHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesBoard()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var command = new CreateBoard(
            ProjectId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "  Sprint 1  "); // leading/trailing whitespace trimmed by Board.Create

        Guid boardId = await new CreateBoardHandler(db).HandleAsync(command);

        Board reloaded = await db.Boards.SingleAsync(b => b.Id == boardId);
        Assert.Equal(command.ProjectId, reloaded.ProjectId);
        Assert.Equal(command.CreatedByUserId, reloaded.CreatedByUserId);
        Assert.Equal("Sprint 1", reloaded.Name);
        Assert.False(reloaded.IsArchived);
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CreateBoardHandler(db)
                .HandleAsync(new CreateBoard(Guid.CreateVersion7(), Guid.CreateVersion7(), "")));
    }

    [Fact]
    public async Task HandleAsync_EmptyProjectId_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CreateBoardHandler(db)
                .HandleAsync(new CreateBoard(Guid.Empty, Guid.CreateVersion7(), "Sprint 1")));
    }
}
