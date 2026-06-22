using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Tasks;

/// <summary>
/// Verifies <see cref="Comment"/> aggregate invariants:
/// factory validation, content edits, and the soft-delete edit guard.
/// </summary>
public sealed class CommentTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    #region Create
    [Fact]
    public void Create_ValidArguments_CommentIsNotEdited()
    {
        Comment comment = CommentBuilder.Create(content: "  Good find.  ");

        Assert.False(comment.IsEdited);
        Assert.Equal("Good find.", comment.Content);
    }

    [Fact]
    public void Create_EmptyContent_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CommentBuilder.Create(content: " "));
    }

    [Fact]
    public void Create_EmptyTaskId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CommentBuilder.Create(taskId: Guid.Empty));
    }

    [Fact]
    public void Create_EmptyAuthorId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CommentBuilder.Create(authorId: Guid.Empty));
    }
    #endregion

    #region UpdateContent
    [Fact]
    public void UpdateContent_ValidContent_UpdatesContentAndSetsIsEdited()
    {
        Comment comment = CommentBuilder.Create();

        comment.UpdateContent("Updated content.");

        Assert.Equal("Updated content.", comment.Content);
        Assert.True(comment.IsEdited);
    }

    [Fact]
    public void UpdateContent_EmptyContent_ThrowsArgumentException()
    {
        Comment comment = CommentBuilder.Create();

        Assert.Throws<ArgumentException>(() => comment.UpdateContent(" "));
    }

    [Fact]
    public void UpdateContent_SoftDeletedComment_ThrowsDomainException()
    {
        Comment comment = CommentBuilder.Create();
        comment.SoftDelete(Now);

        DomainException ex = Assert.Throws<DomainException>(() => comment.UpdateContent("Too late."));

        Assert.Equal("comment.update-deleted", ex.ErrorCode);
    }
    #endregion
}
