using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="RemoveCommentHandler"/> soft-deletes a comment on <see cref="ProjectTask"/>
/// and raises <see cref="CommentRemoved"/>, and correctly surfaces not-found and domain errors.
/// </summary>
public class RemoveCommentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_RemovesCommentAndRaisesCommentRemovedEvent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment comment = task.AddComment("Comment to be removed.", authorId, now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new RemoveCommentHandler(db, new FakeTimeProvider(Now));

        await handler.HandleAsync(
            new RemoveComment(TaskId: task.Id, CommentId: comment.Id, RequestedByUserId: authorId),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Comment removedComment = Assert.Single(reloaded.Comments);
        Assert.True(removedComment.IsDeleted);

        CommentRemoved evt = Assert.Single(reloaded.DomainEvents.OfType<CommentRemoved>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(comment.Id, evt.CommentId);
        Assert.Equal(authorId, evt.RemovedByUserId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new RemoveCommentHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new RemoveComment(TaskId: Guid.CreateVersion7(), CommentId: Guid.CreateVersion7(), RequestedByUserId: Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownCommentId_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment comment = task.AddComment("Comment to be removed.", authorId, now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new RemoveCommentHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new RemoveComment(TaskId: task.Id, CommentId: Guid.CreateVersion7(), RequestedByUserId: authorId),
                CancellationToken.None));
    }
}
