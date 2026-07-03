using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Commands;

/// <summary>
/// Verifies <see cref="AddCommentHandler"/> adds a comment to <see cref="ProjectTask"/>,
/// raises <see cref="CommentAdded"/>, and returns the new comment ID.
/// </summary>
public class AddCommentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ValidCommand_AddsCommentAndRaisesCommentAddedEvent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new AddCommentHandler(db, new FakeTimeProvider(Now));

        Guid commentId = await handler.HandleAsync(
            new AddComment(TaskId: task.Id, AuthorId: authorId, Content: "This is a comment."),
            CancellationToken.None);

        ProjectTask reloaded = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Single(reloaded.Comments);
        Assert.Equal("This is a comment.", reloaded.Comments.First().Content);
        Assert.Equal(commentId, reloaded.Comments.First().Id);

        CommentAdded evt = Assert.Single(reloaded.DomainEvents.OfType<CommentAdded>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(authorId, evt.AuthorId);
        Assert.Equal(task.Comments.First().Id, evt.CommentId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new AddCommentHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new AddComment(TaskId: Guid.CreateVersion7(), AuthorId: Guid.CreateVersion7(), Content: "This is a comment."),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyContent_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        ProjectTask task = TaskBuilder.Create(now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new AddCommentHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(
                new AddComment(TaskId: task.Id, AuthorId: Guid.CreateVersion7(), Content: string.Empty),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TaskDeleted_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var deletedBy = Guid.CreateVersion7();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Delete(deletedBy, Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new AddCommentHandler(db, new FakeTimeProvider(Now));

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(
                new AddComment(TaskId: task.Id, AuthorId: authorId, Content: "This is a comment."),
                CancellationToken.None));
    }
}
