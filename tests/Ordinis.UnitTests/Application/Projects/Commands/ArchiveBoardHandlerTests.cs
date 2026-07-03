using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class ArchiveBoardHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_ArchivesBoard()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await new ArchiveBoardHandler(db).HandleAsync(new ArchiveBoard(board.Id));

        Board reloaded = await db.Boards.SingleAsync(b => b.Id == board.Id);
        Assert.True(reloaded.IsArchived);
    }

    [Fact]
    public async Task HandleAsync_NonExistentBoard_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ArchiveBoardHandler(db).HandleAsync(new ArchiveBoard(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_AlreadyArchived_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        board.Archive();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new ArchiveBoardHandler(db).HandleAsync(new ArchiveBoard(board.Id)));
    }
}
