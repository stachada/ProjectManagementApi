using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Projects;

/// <summary>
/// Verifies <see cref="Board"/> aggregate invariants:
/// factory validation and archived-board guards.
/// </summary>
public sealed class BoardTests
{
    #region Create
    [Fact]
    public void Create_ValidArguments_BoardIsActive()
    {
        Board board = BoardBuilder.Create(name: "  Sprint 1  ");

        Assert.False(board.IsArchived);
        Assert.Equal("Sprint 1", board.Name);
    }

    [Fact]
    public void Create_EmptyName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BoardBuilder.Create(name: " "));
    }

    [Fact]
    public void Create_EmptyProjectId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BoardBuilder.Create(projectId: Guid.Empty));
    }
    #endregion

    #region Rename
    [Fact]
    public void Rename_ActiveBoard_UpdatesName()
    {
        Board board = BoardBuilder.Create();

        board.Rename("New Name");

        Assert.Equal("New Name", board.Name);
    }

    [Fact]
    public void Rename_EmptyName_ThrowsArgumentException()
    {
        Board board = BoardBuilder.Create();

        Assert.Throws<ArgumentException>(() => board.Rename(" "));
    }

    [Fact]
    public void Rename_ArchivedBoard_ThrowsDomainException()
    {
        Board board = BoardBuilder.Create();
        board.Archive();

        DomainException ex = Assert.Throws<DomainException>(() => board.Rename("New Name"));

        Assert.Equal("board.archived", ex.ErrorCode);
    }
    #endregion

    #region UpdateDescription
    [Fact]
    public void UpdateDescription_ActiveBoard_UpdatesDescription()
    {
        Board board = BoardBuilder.Create();

        board.UpdateDescription("New description");

        Assert.Equal("New description", board.Description);
    }

    [Fact]
    public void UpdateDescription_ArchivedBoard_ThrowsDomainException()
    {
        Board board = BoardBuilder.Create();
        board.Archive();

        DomainException ex = Assert.Throws<DomainException>(() => board.UpdateDescription("New description"));

        Assert.Equal("board.archived", ex.ErrorCode);
    }
    #endregion

    #region Archive / Unarchive
    [Fact]
    public void Archive_ActiveBoard_SetsIsArchivedTrue()
    {
        Board board = BoardBuilder.Create();

        board.Archive();

        Assert.True(board.IsArchived);
    }

    [Fact]
    public void Archive_AlreadyArchived_ThrowsDomainException()
    {
        Board board = BoardBuilder.Create();
        board.Archive();

        DomainException ex = Assert.Throws<DomainException>(() => board.Archive());

        Assert.Equal("board.already-archived", ex.ErrorCode);
    }

    [Fact]
    public void Unarchive_ArchivedBoard_SetsIsArchivedFalse()
    {
        Board board = BoardBuilder.Create();
        board.Archive();

        board.Unarchive();

        Assert.False(board.IsArchived);
    }

    [Fact]
    public void Unarchive_NotArchived_ThrowsDomainException()
    {
        Board board = BoardBuilder.Create();

        DomainException ex = Assert.Throws<DomainException>(() => board.Unarchive());

        Assert.Equal("board.not-archived", ex.ErrorCode);
    }
    #endregion
}
