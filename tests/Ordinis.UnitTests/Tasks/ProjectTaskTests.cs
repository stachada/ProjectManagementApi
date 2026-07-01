using Ordinis.Domain.Common;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Tasks;

// <summary>
/// Verifies <see cref="ProjectTask"/> aggregate invariants:
/// factory behaviour, event sourcing, state machine guards,
/// terminal state blocking, and child entity ownership rules.
/// </summary>
public class ProjectTaskTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    #region Create
    [Fact]
    public void Create_ValidArguments_TaskStartsInBacklog()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);

        Assert.Equal(ProjectTaskStatus.Backlog, task.Status);
    }

    [Fact]
    public void Create_ValidArguments_RaisesTaskCreatedEvent()
    {
        var boardId = Guid.CreateVersion7();
        var reporterId = Guid.CreateVersion7();

        ProjectTask task = TaskBuilder.Create(boardId: boardId, reporterId: reporterId, now: Now);

        TaskCreated evt = Assert.Single(task.DomainEvents.OfType<TaskCreated>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(boardId, evt.BoardId);
        Assert.Equal(reporterId, evt.ReporterId);
    }

    [Fact]
    public void Create_PastDueDate_ThrowsDomainException()
    {
        DateTimeOffset pastDate = Now.AddDays(-1);

        DomainException ex = Assert.Throws<DomainException>(() => TaskBuilder.Create(dueDate: pastDate, now: Now));

        Assert.Equal("task.due-date-in-past", ex.ErrorCode);
    }
    #endregion

    #region Move
    [Fact]
    public void Move_ValidTransition_UpdatesStatus()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var userId = Guid.CreateVersion7();

        task.Move(ProjectTaskStatus.ToDo, userId, Now);

        Assert.Equal(ProjectTaskStatus.ToDo, task.Status);
    }

    [Fact]
    public void Move_ValidTransition_RaisedTaskMovedEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var userId = Guid.CreateVersion7();

        task.Move(ProjectTaskStatus.ToDo, userId, Now);

        TaskMoved evt = Assert.Single(task.DomainEvents.OfType<TaskMoved>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(ProjectTaskStatus.Backlog, evt.PreviousStatus);
        Assert.Equal(ProjectTaskStatus.ToDo, evt.NewStatus);
        Assert.Equal(userId, evt.MovedByUserId);
    }

    [Fact]
    public void Move_InvalidTransition_ThrowsDomainException()
    {
        ProjectTask task = TaskBuilder.Create(now: Now); // Backlog

        DomainException ex = Assert.Throws<DomainException>(() => task.Move(ProjectTaskStatus.Done, Guid.CreateVersion7(), Now));

        Assert.Equal("task.invalid-status-transition", ex.ErrorCode);
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void Move_FromTerminalState_ThrowsDomainException(ProjectTaskStatus terminalStatus)
    {
        ProjectTask task = BuildTaskInStatus(terminalStatus);

        DomainException ex = Assert.Throws<DomainException>(() => task.Move(ProjectTaskStatus.ToDo, Guid.CreateVersion7(), Now));
        Assert.Equal("task.invalid-status-transition", ex.ErrorCode);
    }
    #endregion

    #region Update
    [Fact]
    public void Update_ActiveTask_UpdatesAllScalarFields()
    {
        ProjectTask task = TaskBuilder.Create(priority: Priority.Low, now: Now);
        DateTimeOffset newDue = Now.AddDays(14);

        task.Update("New Title", "New Description", Priority.Critical, newDue, Guid.CreateVersion7(), Now);

        Assert.Equal("New Title", task.Title);
        Assert.Equal("New Description", task.Description);
        Assert.Equal(Priority.Critical, task.Priority);
        Assert.Equal(newDue, task.DueDate);
    }

    [Fact]
    public void Update_NullDueDate_ClearsExistingDueDate()
    {
        ProjectTask task = TaskBuilder.Create(dueDate: Now.AddDays(7), now: Now);

        task.Update("New Title", null, Priority.Medium, null, Guid.CreateVersion7(), Now);

        Assert.Null(task.DueDate);
    }

    [Fact]
    public void Update_ValidArguments_RaisesTaskUpdatedEventWithChangedFieldsOnly()
    {
        // Default task: Title = "Default Task Title", Description = null, Priority = Medium, DueDate = Now.AddDays(7)
        ProjectTask task = TaskBuilder.Create(now: Now);
        var updatedBy = Guid.CreateVersion7();

        task.Update("New Title", null, Priority.High, null, updatedBy, Now);

        TaskUpdated evt = Assert.Single(task.DomainEvents.OfType<TaskUpdated>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(updatedBy, evt.UpdatedByUserId);
        Assert.Equal(Now, evt.OccurredAt);

        // Description was null before and after, so it's omitted from Changes entirely.
        Assert.Equal(["Title", "Priority", "DueDate"], evt.Changes.Keys.ToHashSet());

        Assert.Equal("Default Task Title", evt.Changes["Title"].Before);
        Assert.Equal("New Title", evt.Changes["Title"].After);

        Assert.Equal(Priority.Medium, evt.Changes["Priority"].Before);
        Assert.Equal(Priority.High, evt.Changes["Priority"].After);

        Assert.Equal(Now.AddDays(7), evt.Changes["DueDate"].Before);
        Assert.Null(evt.Changes["DueDate"].After);
    }

    [Fact]
    public void Update_OnlyOneFieldActuallyChanges_RaisesEventWithSingleChangeEntry()
    {
        DateTimeOffset unchangedDueDate = Now.AddDays(7);
        ProjectTask task = TaskBuilder.Create(title: "Same Title", description: null, priority: Priority.Medium, dueDate: unchangedDueDate, now: Now);

        task.Update("Same Title", null, Priority.Critical, unchangedDueDate, Guid.CreateVersion7(), Now);

        TaskUpdated evt = Assert.Single(task.DomainEvents.OfType<TaskUpdated>());
        KeyValuePair<string, (object? Before, object? After)> onlyChange = Assert.Single(evt.Changes);
        Assert.Equal("Priority", onlyChange.Key);
        Assert.Equal(Priority.Medium, onlyChange.Value.Before);
        Assert.Equal(Priority.Critical, onlyChange.Value.After);
    }

    [Fact]
    public void Update_PastDueDate_ThrowsDomainExceptionAndRaisesNoEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);

        DomainException ex = Assert.Throws<DomainException>(
            () => task.Update("New Title", null, Priority.High, Now.AddSeconds(-1), Guid.CreateVersion7(), Now));

        Assert.Equal("task.due-date-in-past", ex.ErrorCode);
        Assert.Empty(task.DomainEvents.OfType<TaskUpdated>());
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void Update_TerminalTask_ThrowsDomainException(ProjectTaskStatus terminalStatus)
    {
        ProjectTask task = BuildTaskInStatus(terminalStatus);

        Assert.Throws<DomainException>(
            () => task.Update("Ignored", null, Priority.High, null, Guid.CreateVersion7(), Now));
    }
    #endregion

    #region Assign / Unassign
    [Fact]
    public void Assign_ActiveUnassignedTask_SetAssigneeAndRaisesEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var assignedId = Guid.CreateVersion7();
        var assignedBy = Guid.CreateVersion7();

        task.Assign(assignedId, assignedBy, Now);

        Assert.Equal(assignedId, task.AssigneeId);
        TaskAssigned evt = Assert.Single(task.DomainEvents.OfType<TaskAssigned>());
        Assert.Equal(assignedId, evt.AssigneeId);
        Assert.Equal(assignedBy, evt.AssignedByUserId);
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void Assign_TerminalTask_ThrowsDomainException(ProjectTaskStatus terminalStatus)
    {
        ProjectTask task = BuildTaskInStatus(terminalStatus);

        Assert.Throws<DomainException>(() => task.Assign(Guid.CreateVersion7(), Guid.CreateVersion7(), Now));
    }

    [Fact]
    public void Unassign_AssignedTask_ClearsAssigneeAndRaisesEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var assigneeId = Guid.CreateVersion7();
        task.Assign(assigneeId, Guid.CreateVersion7(), Now);

        task.Unassign(Guid.CreateVersion7(), Now);

        Assert.Null(task.AssigneeId);
        TaskUnassigned evt = Assert.Single(task.DomainEvents.OfType<TaskUnassigned>());
        Assert.Equal(assigneeId, evt.PreviousAssigneeId);
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void Unassign_TerminalTask_ThrowsDomainException(ProjectTaskStatus terminalStatus)
    {
        ProjectTask task = BuildTaskInStatus(terminalStatus);

        Assert.Throws<DomainException>(() => task.Unassign(Guid.CreateVersion7(), Now));
    }
    #endregion

    #region AddComment
    [Fact]
    public void AddComment_ActiveTask_AddsCommentAndRaisesEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var authorId = Guid.CreateVersion7();

        task.AddComment("Good find.", authorId, Now);

        Assert.Single(task.Comments);
        CommentAdded evt = Assert.Single(task.DomainEvents.OfType<CommentAdded>());
        Assert.Equal(authorId, evt.AuthorId);
    }

    [Fact]
    public void AddComment_SoftDeletedTask_ThrowsDomainException()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        task.SoftDelete(Now);

        DomainException ex = Assert.Throws<DomainException>(() => task.AddComment("Too late.", Guid.CreateVersion7(), Now));

        Assert.Equal("task.deleted", ex.ErrorCode);
    }
    #endregion

    #region RemoveComment
    [Fact]
    public void RemoveComment_ExistingComment_SoftDeletesItAndRaisesEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var authorId = Guid.CreateVersion7();
        task.AddComment("Original.", authorId, Now);
        Comment comment = task.Comments.Single();

        task.RemoveComment(comment.Id, Guid.CreateVersion7(), Now);

        Assert.True(comment.IsDeleted);
        CommentRemoved evt = Assert.Single(task.DomainEvents.OfType<CommentRemoved>());
        Assert.Equal(comment.Id, evt.CommentId);
    }

    [Fact]
    public void RemoveComment_NonExistentComment_ThrowsDomainException()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);

        DomainException ex = Assert.Throws<DomainException>(() =>
            task.RemoveComment(Guid.CreateVersion7(), Guid.CreateVersion7(), Now));

        Assert.Equal("task.comment-not-found", ex.ErrorCode);
    }
    #endregion

    #region AddAttachment / RemoveAttachment
    [Fact]
    public void AddAttachment_ActiveTask_AddsAttachmentAndRaisesEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var uploaderId = Guid.CreateVersion7();

        task.AddAttachment("report.pdf", "application/pdf", 12_345, "blobs/report.pdf", uploaderId, Now);

        Assert.Single(task.Attachments);
        AttachmentAdded evt = Assert.Single(task.DomainEvents.OfType<AttachmentAdded>());
        Assert.Equal("report.pdf", evt.FileName);
        Assert.Equal(uploaderId, evt.UploadedByUserId);
    }

    [Fact]
    public void AddAttachment_SoftDeletedTask_ThrowsDomainException()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        task.SoftDelete(Now);

        DomainException ex = Assert.Throws<DomainException>(() =>
            task.AddAttachment("x.pdf", "application/pdf", 1, "url", Guid.CreateVersion7(), Now));

        Assert.Equal("task.deleted", ex.ErrorCode);
    }

    [Fact]
    public void RemoveAttachment_ExistingAttachment_RemovesItAndRaisesEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var uploaderId = Guid.CreateVersion7();
        task.AddAttachment("report.pdf", "application/pdf", 1, "blobs/x", uploaderId, Now);
        Attachment attachment = task.Attachments.Single();
        var removerId = Guid.CreateVersion7();

        task.RemoveAttachment(attachment.Id, removerId, Now);

        Assert.Empty(task.Attachments);
        AttachmentRemoved evt = Assert.Single(task.DomainEvents.OfType<AttachmentRemoved>());
        Assert.Equal(attachment.Id, evt.AttachmentId);
        Assert.Equal(removerId, evt.RemovedByUserId);
    }
    #endregion

    #region Delete
    [Fact]
    public void Delete_ActiveTask_SetsIsDeletedAndRaisesTaskDeletedEvent()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var deletedBy = Guid.CreateVersion7();

        task.Delete(deletedBy, Now);

        Assert.True(task.IsDeleted);
        Assert.Equal(Now, task.DeletedAt);

        TaskDeleted evt = Assert.Single(task.DomainEvents.OfType<TaskDeleted>());
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(deletedBy, evt.DeletedByUserId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public void Delete_AlreadyDeletedTask_IsNoOp()
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        task.Delete(Guid.CreateVersion7(), Now);

        task.Delete(Guid.CreateVersion7(), Now);

        Assert.Single(task.DomainEvents.OfType<TaskDeleted>());
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Done)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public void Delete_TerminalTask_SoftDeletesAndRaisesEvent(ProjectTaskStatus terminalStatus)
    {
        ProjectTask task = BuildTaskInStatus(terminalStatus);

        task.Delete(Guid.CreateVersion7(), Now);

        Assert.True(task.IsDeleted);
        Assert.Single(task.DomainEvents.OfType<TaskDeleted>());
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Builds a <see cref="ProjectTask"/> already in the requested status by
    /// applying the minimum legal sequence of transitions. Keeps test bodies clean.
    /// </summary>
    private static ProjectTask BuildTaskInStatus(ProjectTaskStatus status)
    {
        ProjectTask task = TaskBuilder.Create(now: Now);
        var userId = Guid.CreateVersion7();

        // Walk through the happy-path sequence to the target state
        ProjectTaskStatus[] path = status switch
        {
            ProjectTaskStatus.ToDo => new[] { ProjectTaskStatus.ToDo },
            ProjectTaskStatus.InProgress => new[] { ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress },
            ProjectTaskStatus.InReview => new[] { ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview },
            ProjectTaskStatus.Done => new[] { ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview, ProjectTaskStatus.Done },
            ProjectTaskStatus.Cancelled => new[] { ProjectTaskStatus.Cancelled },
            _ => Array.Empty<ProjectTaskStatus>()
        };

        foreach (ProjectTaskStatus nextStatus in path)
        {
            task.Move(nextStatus, userId, Now);
        }

        return task;
    }
    #endregion
}
