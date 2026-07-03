using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="EditCommentHandler"/> updates comment content and sets <c>IsEdited</c>,
/// and correctly surfaces not-found and domain errors.
/// </summary>
public class EditCommentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_EditsComment()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment comment = task.AddComment("Original comment.", authorId, now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new EditCommentHandler(db);

        await handler.HandleAsync(
            new EditComment(TaskId: task.Id, CommentId: comment.Id, NewContent: "Edited comment.", RequestedByUserId: authorId),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal("Edited comment.", reloaded.Comments.First().Content);
        Assert.True(reloaded.Comments.First().IsEdited);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new EditCommentHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new EditComment(TaskId: Guid.CreateVersion7(), CommentId: Guid.CreateVersion7(), NewContent: "Edited comment.", RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownCommentId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new EditCommentHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new EditComment(TaskId: task.Id, CommentId: Guid.CreateVersion7(), NewContent: "Edited comment.", RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyNewContent_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment comment = task.AddComment("Original comment.", authorId, now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new EditCommentHandler(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(
                new EditComment(TaskId: task.Id, CommentId: comment.Id, NewContent: "", RequestedByUserId: authorId),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DeletedComment_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment comment = task.AddComment("Original comment.", authorId, now: Now);
        task.RemoveComment(comment.Id, authorId, now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new EditCommentHandler(db);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new EditComment(TaskId: task.Id, CommentId: comment.Id, NewContent: "Edited comment.", RequestedByUserId: authorId),
                CancellationToken.None));
    }
}
