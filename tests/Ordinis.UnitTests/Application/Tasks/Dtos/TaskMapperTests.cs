using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Dtos;

/// <summary>
/// Verifies <see cref="TaskMapper"/> field mapping. Pure function tests -
/// no EF Core, no DI, no async; comments and attachments are attached via
/// <see cref="ProjectTask"/>'s own public API rather than constructed directly.
/// </summary>
public class TaskMapperTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    #region ToSummaryDto
    [Fact]
    public void ToSummaryDto_AllFieldsMapCorrectly()
    {
        Guid assigneeId = Guid.CreateVersion7();
        ProjectTask task = TaskBuilder.Create(
            title: "Fix login redirect bug",
            description: "Users land on 404 after SSO callback",
            priority: Priority.High,
            now: Now);
        task.Assign(assigneeId, Guid.CreateVersion7(), Now);
        task.CreatedAt = Now;
        task.UpdatedAt = Now.AddHours(1);
        var userLookup = new Dictionary<Guid, string> { [assigneeId] = "Ada Lovelace" };

        TaskSummaryDto dto = task.ToSummaryDto(userLookup);

        Assert.Equal(task.Id, dto.Id);
        Assert.Equal(task.BoardId, dto.BoardId);
        Assert.Equal("Fix login redirect bug", dto.Title);
        Assert.Equal(ProjectTaskStatus.Backlog, dto.Status);
        Assert.Equal(Priority.High, dto.Priority);
        Assert.Equal(assigneeId, dto.AssigneeId);
        Assert.Equal("Ada Lovelace", dto.AssigneeDisplayName);
        Assert.Equal(task.DueDate, dto.DueDate);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
    }

    [Fact]
    public void ToSummaryDto_UnassignedTask_AssigneeIdAndDisplayNameAreNull()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var userLookup = new Dictionary<Guid, string>();

        TaskSummaryDto dto = task.ToSummaryDto(userLookup);

        Assert.Null(dto.AssigneeId);
        Assert.Null(dto.AssigneeDisplayName);
    }

    [Fact]
    public void ToSummaryDto_CountsExcludeSoftDeletedComments_AttachmentsHaveNoSoftDelete()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment keptComment = task.AddComment("Looks good", Guid.CreateVersion7(), Now);
        Comment removedComment = task.AddComment("Retracted", Guid.CreateVersion7(), Now);
        task.RemoveComment(removedComment.Id, Guid.CreateVersion7(), Now);
        task.AddAttachment("a.png", "image/png", 100, "blob://a", Guid.CreateVersion7(), Now);
        task.AddAttachment("b.png", "image/png", 200, "blob://b", Guid.CreateVersion7(), Now);

        TaskSummaryDto dto = task.ToSummaryDto(new Dictionary<Guid, string>());

        Assert.Equal(1, dto.CommentCount);
        Assert.Equal(2, dto.AttachmentCount);
        Assert.NotEqual(removedComment.Id, keptComment.Id);
    }
    #endregion

    #region ToDto - core fields
    [Fact]
    public void ToDto_AllFieldsMapCorrectly()
    {
        Guid assigneeId = Guid.CreateVersion7();
        ProjectTask task = TaskBuilder.Create(
            title: "Fix login redirect bug",
            description: "Users land on 404 after SSO callback",
            priority: Priority.High,
            now: Now);
        task.Assign(assigneeId, Guid.CreateVersion7(), Now);
        task.CreatedAt = Now;
        task.UpdatedAt = Now.AddHours(1);
        var userLookup = new Dictionary<Guid, string> { [assigneeId] = "Ada Lovelace" };

        TaskDto dto = task.ToDto(userLookup);

        Assert.Equal(task.Id, dto.Id);
        Assert.Equal(task.BoardId, dto.BoardId);
        Assert.Equal("Fix login redirect bug", dto.Title);
        Assert.Equal("Users land on 404 after SSO callback", dto.Description);
        Assert.Equal(ProjectTaskStatus.Backlog, dto.Status);
        Assert.Equal(Priority.High, dto.Priority);
        Assert.Equal(assigneeId, dto.AssigneeId);
        Assert.Equal("Ada Lovelace", dto.AssigneeDisplayName);
        Assert.Equal(task.DueDate, dto.DueDate);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
        Assert.Empty(dto.Comments);
        Assert.Empty(dto.Attachments);
    }

    [Fact]
    public void ToDto_UnpersistedTask_ConcurrencyTokenIsEmptyString()
    {
        // RowVersion is only set by EF Core on save; ToDto must stay safe for
        // unpersisted instances used throughout this test class.
        ProjectTask task = TaskBuilder.Create(now: Now);

        TaskDto dto = task.ToDto(new Dictionary<Guid, string>());

        Assert.Equal(string.Empty, dto.ConcurrencyToken);
    }
    #endregion

    #region ToDto - comments
    [Fact]
    public void ToDto_Comments_MapAllFieldsAndExcludeSoftDeleted_OrderedByCreatedAt()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        Guid authorId = Guid.CreateVersion7();
        Comment first = task.AddComment("First", authorId, Now);
        Comment second = task.AddComment("Second", authorId, Now.AddMinutes(5));
        Comment removed = task.AddComment("Removed", authorId, Now.AddMinutes(10));
        task.RemoveComment(removed.Id, Guid.CreateVersion7(), Now.AddMinutes(15));
        first.CreatedAt = Now;
        second.CreatedAt = Now.AddMinutes(5);
        var userLookup = new Dictionary<Guid, string> { [authorId] = "Grace Hopper" };

        TaskDto dto = task.ToDto(userLookup);

        Assert.Equal(2, dto.Comments.Count);
        Assert.Equal(["First", "Second"], dto.Comments.Select(c => c.Content));
        CommentDto mappedFirst = dto.Comments[0];
        Assert.Equal(first.Id, mappedFirst.Id);
        Assert.Equal(authorId, mappedFirst.AuthorId);
        Assert.Equal("Grace Hopper", mappedFirst.AuthorDisplayName);
        Assert.Equal(Now, mappedFirst.CreatedAt);
    }

    [Fact]
    public void ToDto_CommentIsEdited_DerivedFromDomainIsEditedFlag()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        Comment untouched = task.AddComment("Original", Guid.CreateVersion7(), Now);
        Comment edited = task.AddComment("Original", Guid.CreateVersion7(), Now);
        edited.UpdateContent("Edited content");
        edited.UpdatedAt = Now.AddHours(2);

        TaskDto dto = task.ToDto(new Dictionary<Guid, string>());

        CommentDto untouchedDto = Assert.Single(dto.Comments, c => c.Id == untouched.Id);
        CommentDto editedDto = Assert.Single(dto.Comments, c => c.Id == edited.Id);
        Assert.False(untouchedDto.IsEdited);
        Assert.Null(untouchedDto.UpdatedAt);
        Assert.True(editedDto.IsEdited);
        Assert.Equal("Edited content", editedDto.Content);
        Assert.Equal(Now.AddHours(2), editedDto.UpdatedAt);
    }
    #endregion

    #region ToDto - attachments
    [Fact]
    public void ToDto_Attachments_MapAllFields_OrderedByUploadedAt()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        Attachment second = task.AddAttachment(
            "second.png", "image/png", 200, "blob://second", Guid.CreateVersion7(), Now.AddMinutes(5));
        Attachment first = task.AddAttachment(
            "first.png", "image/png", 100, "blob://first", Guid.CreateVersion7(), Now);

        TaskDto dto = task.ToDto(new Dictionary<Guid, string>());

        Assert.Equal(["first.png", "second.png"], dto.Attachments.Select(a => a.FileName));
        AttachmentDto mappedFirst = dto.Attachments[0];
        Assert.Equal(first.Id, mappedFirst.Id);
        Assert.Equal("image/png", mappedFirst.ContentType);
        Assert.Equal(100, mappedFirst.SizeInBytes);
        Assert.Equal("blob://first", mappedFirst.DownloadUrl);
        Assert.Equal(Now, mappedFirst.UploadedAt);
        Assert.NotEqual(first.Id, second.Id);
    }
    #endregion

    #region ToDto - userLookup resolution
    [Fact]
    public void ToDto_MissingUserInLookup_AssigneeDisplayNameIsNullAndCommentAuthorDisplayNameIsEmpty()
    {
        Guid assigneeId = Guid.CreateVersion7();
        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Assign(assigneeId, Guid.CreateVersion7(), Now);
        task.AddComment("Orphaned author", Guid.CreateVersion7(), Now);
        var emptyLookup = new Dictionary<Guid, string>();

        TaskDto dto = task.ToDto(emptyLookup);

        Assert.Null(dto.AssigneeDisplayName);
        Assert.Equal(string.Empty, Assert.Single(dto.Comments).AuthorDisplayName);
    }
    #endregion
}
