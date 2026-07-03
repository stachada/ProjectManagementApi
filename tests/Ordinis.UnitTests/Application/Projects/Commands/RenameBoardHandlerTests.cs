using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Commands;

public class RenameBoardHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_RenamesBoard()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create(name: "Old Name");
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await new RenameBoardHandler(db)
            .HandleAsync(new RenameBoard(board.Id, "  New Name  ")); // whitespace trimmed by Board.Rename

        Board reloaded = await db.Boards.SingleAsync(b => b.Id == board.Id);
        Assert.Equal("New Name", reloaded.Name);
    }

    [Fact]
    public async Task HandleAsync_NonExistentBoard_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RenameBoardHandler(db)
                .HandleAsync(new RenameBoard(Guid.CreateVersion7(), "New Name")));
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RenameBoardHandler(db)
                .HandleAsync(new RenameBoard(board.Id, "")));
    }

    [Fact]
    public async Task HandleAsync_ArchivedBoard_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        board.Archive();
        db.Boards.Add(board);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new RenameBoardHandler(db)
                .HandleAsync(new RenameBoard(board.Id, "New Name")));
    }
}
