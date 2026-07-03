using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Queries;

/// <summary>
/// Verifies <see cref="GetTaskByIdHandler"/> returns a correctly mapped <see cref="TaskDto"/>
/// with resolved user display names and embedded child collections,
/// and throws <see cref="NotFoundException"/> when the task does not exist.
/// </summary>
public class GetTaskByIdHandlerTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_ExistingTask_ReturnsCorrectScalarFields()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var boardId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(
            boardId: boardId,
            title: "Test Task",
            description: "A description",
            priority: Priority.High,
            dueDate: Now.AddDays(7),
            now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new GetTaskByIdHandler(db);
        TaskDto dto = await handler.HandleAsync(new GetTaskById(task.Id), CancellationToken.None);

        Assert.Equal(task.Id, dto.Id);
        Assert.Equal(boardId, dto.BoardId);
        Assert.Equal("Test Task", dto.Title);
        Assert.Equal("A description", dto.Description);
        Assert.Equal(ProjectTaskStatus.Backlog, dto.Status);
        Assert.Equal(Priority.High, dto.Priority);
        Assert.Null(dto.AssigneeId);
        Assert.Null(dto.AssigneeDisplayName);
        Assert.Equal(Now.AddDays(7), dto.DueDate);
        Assert.Empty(dto.Comments);
        Assert.Empty(dto.Attachments);
        // ConcurrencyToken (Base64 RowVersion) is empty in InMemory tests — the InMemory
        // provider does not generate row-version bytes. Covered by integration tests.
    }

    [Fact]
    public async Task HandleAsync_AssignedTask_ResolvesAssigneeDisplayName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var assignee = UserBuilder.Create(displayName: "Alice Smith");
        db.Users.Add(assignee);

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(assignee.Id, Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new GetTaskByIdHandler(db);
        TaskDto dto = await handler.HandleAsync(new GetTaskById(task.Id), CancellationToken.None);

        Assert.Equal(assignee.Id, dto.AssigneeId);
        Assert.Equal("Alice Smith", dto.AssigneeDisplayName);
    }

    [Fact]
    public async Task HandleAsync_TaskWithComment_ReturnsCommentWithAuthorDisplayNameAndCorrectFields()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var author = UserBuilder.Create(displayName: "Bob Jones");
        db.Users.Add(author);

        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment comment = task.AddComment("Nice work.", author.Id, now: Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new GetTaskByIdHandler(db);
        TaskDto dto = await handler.HandleAsync(new GetTaskById(task.Id), CancellationToken.None);

        CommentDto commentDto = Assert.Single(dto.Comments);
        Assert.Equal(comment.Id, commentDto.Id);
        Assert.Equal(author.Id, commentDto.AuthorId);
        Assert.Equal("Bob Jones", commentDto.AuthorDisplayName);
        Assert.Equal("Nice work.", commentDto.Content);
        Assert.False(commentDto.IsEdited);
        Assert.Null(commentDto.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_TaskWithSoftDeletedComment_ExcludesSoftDeletedCommentFromDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var authorId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(now: Now);
        task.AddComment("Kept.", authorId, now: Now);
        Comment removed = task.AddComment("Removed.", authorId, now: Now);
        task.RemoveComment(removed.Id, authorId, Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new GetTaskByIdHandler(db);
        TaskDto dto = await handler.HandleAsync(new GetTaskById(task.Id), CancellationToken.None);

        CommentDto only = Assert.Single(dto.Comments);
        Assert.Equal("Kept.", only.Content);
    }

    [Fact]
    public async Task HandleAsync_TaskWithAttachment_ReturnsAttachmentDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        ProjectTask task = TaskBuilder.Create(now: Now);
        Attachment attachment = task.AddAttachment("report.pdf", "application/pdf", 12_345, "blobs/report.pdf", Guid.CreateVersion7(), Now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var handler = new GetTaskByIdHandler(db);
        TaskDto dto = await handler.HandleAsync(new GetTaskById(task.Id), CancellationToken.None);

        AttachmentDto attDto = Assert.Single(dto.Attachments);
        Assert.Equal(attachment.Id, attDto.Id);
        Assert.Equal("report.pdf", attDto.FileName);
        Assert.Equal("application/pdf", attDto.ContentType);
        Assert.Equal(12_345, attDto.SizeInBytes);
        Assert.Equal("blobs/report.pdf", attDto.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_UnknownTaskId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new GetTaskByIdHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetTaskById(Guid.CreateVersion7()), CancellationToken.None));
    }
}
