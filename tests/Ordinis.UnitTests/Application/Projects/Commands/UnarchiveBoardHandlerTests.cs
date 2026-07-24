using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class UnarchiveBoardHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UnarchivesBoard()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        board.Archive();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await new UnarchiveBoardHandler(db).HandleAsync(new UnarchiveBoard(board.Id, board.RowVersion));

        Board reloaded = await db.Boards.SingleAsync(b => b.Id == board.Id);
        Assert.False(reloaded.IsArchived);
    }

    [Fact]
    public async Task HandleAsync_NonExistentBoard_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UnarchiveBoardHandler(db).HandleAsync(new UnarchiveBoard(Guid.CreateVersion7(), null)));
    }

    [Fact]
    public async Task HandleAsync_NotArchived_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new UnarchiveBoardHandler(db).HandleAsync(new UnarchiveBoard(board.Id, board.RowVersion)));
    }
}
