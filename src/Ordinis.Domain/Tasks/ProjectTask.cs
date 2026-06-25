using System.Security.Cryptography;
using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Represents a unit of work within a board - the core entity of the system.
/// </summary>
/// <remarks>
/// <para>
/// <b>Naming:</b> The class is named <c>ProjectTask</c> rather than <c>Task</c>
/// to avoid ambiguity with <see cref="System.Threading.Tasks.Task"/>. The REST
/// resource is still exposed as <c>/tasks</c> and EF Core's <c>DbSet</c> is
/// named <c>Tasks</c> in <c>AppDbContext</c>.
/// </para>
/// <para>
/// <b>Aggregate root:</b> <c>ProjectTask</c> is the primary consistency boundary
/// in the domain. All mutations - status transitions, assignment, comments,
/// attachments - go through methods on this class. External code, never mutates
/// child entities (<see cref="Comment"/> and <see cref="Attachment"/>) directly.
/// </para>
/// <para>
/// <b>State machine:</b> Status transitions are validated against the adjacency
/// list in <see cref="ProjectTaskStatusExtensions"/>. Calling <see cref="Move"/> with
/// an illegal transition throws a <see cref="DomainException"/> with error code
/// <c>"task.invalid-status-transition"</c>. Terminal states (<c>Done</c> and <c>Cancelled</c>)
/// reject all further transitions.
/// </para>
/// <para>
/// <b>Concurrency:</b> <see cref="AggregateRoot.RowVersion"/> is mapped as a
/// concurrency token in <c>ProjectTaskConfiguration</c>. Command
/// handlers catch <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// and return <c>409 Conflict</c> with a Problem Details body. The ETag header
/// on GET responses carries a base64-encoded representation of <c>RowVersion</c>,
/// and write operations consume it via the <c>If-Match</c> header.
/// </para>
/// <para>
/// <b>Collections:</b> <see cref="Comments"/> and <see cref="Attachments"/> are
/// navigation properties but are <b>never eager-loaded</b> by default. Handlers
/// that need them load them explicitly. This prevents inadvertently pulling large
/// collections when loading a task for a simple status update.
/// </para>
/// </remarks>
public class ProjectTask : AggregateRoot
{
    #region Private backing fields
    private readonly List<Comment> _comments = [];
    private readonly List<Attachment> _attachments = [];
    #endregion

    #region Core properties
    /// <summary>
    /// Short summary of the work to be done. (e.g. "Fix login redirect bug").
    /// Required. Maximum length enforced by <c>CreateTaskValidator</c>
    /// in the Application layer.
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Full description of the task. Supports markdown. Optional.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Current position of the task in its lifecycle.
    /// Transitions are validated by <see cref="TaskStatusExtension.CanTransitionTo"/>
    /// Stored as a <c>varchar</c> string column - never as an integer - so the
    /// database is readable without a lookup table.
    /// </summary>
    public ProjectTaskStatus Status { get; private set; }

    /// <summary>
    /// Urgency level of the task. Used for sorting and filtering.
    /// Stored as a <c>varchar</c> string column.
    /// </summary>
    public Priority Priority { get; private set; }

    /// <summary>
    /// Optional deadline for completing the task.
    /// Used in <c>GET /task?dueBefore=</c> filtering and <c>sort=dueDate</c>.
    /// Stored as UTC.
    /// </summary>
    public DateTimeOffset? DueDate { get; private set; }
    #endregion

    #region Foreign keys
    /// <summary>
    /// The board this task belongs to
    /// </summary>
    public Guid BoardId { get; private set; }

    /// <summary>
    /// The user who created the task. Required. Immutable after creation.
    /// </summary>
    public Guid ReporterId { get; private set; }

    /// <summary>
    /// The user currently responsible for completing the task.
    /// <c>null</c> when the task is unassigned - a valid and common state,
    /// particularly for backlog items.
    /// </summary>
    public Guid? AssigneeId { get; private set; }
    #endregion

    #region Navigation properties
    /// <summary>
    /// The board this task belongs to.
    /// </summary>
    public Board? Board { get; private set; }

    /// <summary>
    /// The user who created this task.
    /// </summary>
    public User? Reporter { get; private set; }

    /// <summary>
    /// The user assigned to this task. <c>null</c> when unassigned.
    /// </summary>
    public User? Assignee { get; private set; }

    /// <summary>
    /// Comments posted on this task. Not eager-loaded - use explicit
    /// loading in handlers that require the comment list.
    /// Mutated exclusively via <see cref="AddComment"/> and
    /// <see cref="RemoveComment"/>.
    /// </summary>
    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();

    /// <summary>
    /// Files attached to this task. Not eager-loaded - use explicit
    /// loading in handlers that require the attachment list.
    /// Mutated exclusively via <see cref="AddAttachment"/> and
    /// <see cref="RemoveAttachment"/>.
    /// </summary>
    public IReadOnlyCollection<Attachment> Attachments => _attachments.AsReadOnly();
    #endregion

    #region Constructor
    private ProjectTask() { }

    /// <summary>
    /// Creates a new task on the given board in <see cref="ProjectTaskStatus.Backlog"/> status.
    /// </summary>
    /// <param name="boardId">The board to create the task on. Must not be empty.</param>
    /// <param name="reporterId">The user creating the task. Must not be empty.</param>
    /// <param name="title">Short task summary. Must not be empty.</param>
    /// <param name="now">The time the task was created. Used for validating <paramref name="dueDate"/>.</param>
    /// <param name="priority">Urgency level. Defaults to <see cref="Priority.Medium"/>.</param>
    /// <param name="description">Optional detailed description.</param>
    /// <param name="dueDate">Optional deadline. Must be a future UTC date if provided.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="title"/> is empty, or if <paramref name="boardId"/>
    /// or <paramref name="reporterId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    /// <exception cref="DomainException">
    /// Thrown if <paramref name="dueDate"/> is in the past.
    /// </exception>
    public static ProjectTask Create(
        Guid boardId,
        Guid reporterId,
        string title,
        DateTimeOffset now,
        Priority priority = Priority.Medium,
        string? description = null,
        DateTimeOffset? dueDate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (boardId == Guid.Empty)
        {
            throw new ArgumentException("BoardId cannot be empty.", nameof(boardId));
        }

        if (reporterId == Guid.Empty)
        {
            throw new ArgumentException("ReporterId cannot be empty.", nameof(reporterId));
        }

        var task = new ProjectTask
        {
            BoardId = boardId,
            ReporterId = reporterId,
            Title = title.Trim(),
            Description = description?.Trim(),
            Priority = priority,
            Status = ProjectTaskStatus.Backlog,
            DueDate = dueDate,
        };

        if (dueDate.HasValue && dueDate.Value <= now)
        {
            throw new DomainException(
                "Due date must be in the future.",
                "task.due-date-in-past"
            );
        }

        task.RaiseDomainEvent(new TaskCreated(
            task.Id,
            boardId,
            reporterId,
            title,
            now));

        return task;
    }
    #endregion

    #region Core task behaviour
    /// <summary>
    /// Updates the task's title and/or description.
    /// </summary>
    /// <param name="title">New title, Must not be empty</param>
    /// <param name="description">New description, or <c>null</c> to clear it.</param>
    /// <exception cref="DomainException">Thrown if the task is in a terminal state.</exception>
    public void UpdateDetails(string title, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EnsureNotTerminal();

        Title = title.Trim();
        Description = description?.Trim();
    }

    /// <summary>
    /// Changes the task's priority.
    /// </summary>
    /// <param name="newPriority">The new priority level.</param>
    /// <exception cref="DomainException">Thrown if the task is in a terminal state.</exception>
    public void ChangePriority(Priority newPriority)
    {
        EnsureNotTerminal();
        Priority = newPriority;
    }

    /// <summary>
    /// Sets or clears the task's due date.
    /// </summary>
    /// <param name="newDueDate">
    /// New due date (must be a future UTC value), or <c>null</c> to remove it.
    /// </param>
    /// <param name="now">
    /// The date and time when the change is occurring.
    /// </param>
    /// <exception cref="DomainException">Thrown if the task is in a terminal state or if the new due date is in the past.</exception>
    public void SetDueDate(DateTimeOffset? newDueDate, DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (newDueDate.HasValue && newDueDate.Value <= now)
        {
            throw new DomainException(
                "Due date must be in the future.",
                "task.due-date-in-past"
            );
        }

        DueDate = newDueDate;
    }
    #endregion

    #region State machine
    /// <summary>
    /// Moves the task to a new status, validating the transition against
    /// the state machine defined in <see cref="ProjectTaskStatusExtensions"/>.
    /// </summary>
    /// <param name="newStatus">The target status.</param>
    /// <param name="movedByUserId">The user requesting the transition. Carried in the domain event.</param>
    /// <param name="now">The date and time when the transition is occurring. Carried in the domain event.</param>
    /// <exception cref="DomainException">
    /// Thrown if the transition from the current status to <paramref name="newStatus"/>
    /// is not permitted by the state machine, or if the task is already in a terminal state.
    /// </exception>
    /// <remarks>
    /// Legal transitions are defined in <see cref="ProjectTaskStatusExtensions.AllowedTransitions"/>.
    /// This method is the only place in the codebase that changes <see cref="Status"/> —
    /// no handler or service sets the property directly.
    /// The HATEOAS layer (Phase 6) calls <see cref="ProjectTaskStatusExtensions.GetAllowedTransitions"/>
    /// on the current status to generate <c>_links</c> for valid next states.
    /// </remarks>
    public void Move(ProjectTaskStatus newStatus, Guid movedByUserId, DateTimeOffset now)
    {
        if (!Status.CanTransitionTo(newStatus))
        {
            throw new DomainException(
                $"Cannot transition task from '{Status}' to '{newStatus}'.",
                "task.invalid-status-transition"
            );
        }

        ProjectTaskStatus previousStatus = Status;
        Status = newStatus;

        RaiseDomainEvent(new TaskMoved(
            Id,
            previousStatus,
            newStatus,
            movedByUserId,
            now));
    }
    #endregion

     #region Assignment
    /// <summary>
    /// Assigns the task to a user, or reassigns it from the current assignee.
    /// </summary>
    /// <param name="assigneeId">
    /// The user to assign the task to. The Application layer is responsible
    /// for verifying that this user is a member of the project before calling
    /// this method.
    /// </param>
    /// <param name="assignedByUserId">The user performing the assignment.</param>
    /// <param name="now">The date and time when the assignment is occurring. Carried in the domain event.</param>
    /// <exception cref="DomainException">
    /// Thrown if the task is in a terminal state, or if the user is already
    /// the current assignee.
    /// </exception>
    public void Assign(Guid assigneeId, Guid assignedByUserId, DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (assigneeId == Guid.Empty)
        {
            throw new ArgumentException("AssigneeId cannot be empty.", nameof(assigneeId));
        }

        if (AssigneeId == assigneeId)
        {
            throw new DomainException(
                "Task is already assigned to this user.",
                "task.already-assigned"
            );
        }

        AssigneeId = assigneeId;

        RaiseDomainEvent(new TaskAssigned(
            Id,
            assigneeId,
            assignedByUserId,
            now));
    }

    /// <summary>
    /// Removes the current assignee, leaving the task unassigned.
    /// </summary>
    /// <exception cref="DomainException">
    /// Thrown if the task is in a terminal state or is already unassigned.
    /// </exception>
    /// <param name="unassignedByUserId">The user performing the unassignment.</param>
    /// <param name="now">The date and time when the unassignment is occurring. Carried in the domain event.</param>
    public void Unassign(Guid unassignedByUserId, DateTimeOffset now)
    {
        EnsureNotTerminal();

        if (AssigneeId is null)
        {
            throw new DomainException(
                "Task is already unassigned.",
                "task.already-unassigned"
            );
        }

        Guid previousAssignedId = AssigneeId.Value;
        AssigneeId = null;

        RaiseDomainEvent(new TaskUnassigned(
            Id,
            previousAssignedId,
            unassignedByUserId,
            now));
    }
    #endregion

    #region Comments
    /// <summary>
    /// Adds a comment to the task.
    /// </summary>
    /// <param name="content">The comment text. Must not be empty.</param>
    /// <param name="authorId">The user posting the comment.</param>
    /// <param name="now">The date and time when the comment is being added. Carried in the domain event.</param>
    /// <returns>The newly created <see cref="Comment"/>.</returns>
    /// <exception cref="DomainException">Thrown if the task is soft-deleted.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="content"/> is empty.</exception>
    public Comment AddComment(string content, Guid authorId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (IsDeleted)
        {
            throw new DomainException(
                "Cannot add a comment to a deleted task.",
                "task.deleted"
            );
        }

        var comment = Comment.Create(Id, authorId, content);
        _comments.Add(comment);

        RaiseDomainEvent(new CommentAdded(
            Id,
            comment.Id,
            authorId,
            now));

        return comment;
    }

    /// <summary>
    /// Removes a comment from the task by soft-deleting it.
    /// </summary>
    /// <param name="commentId">The Id of the comment to remove.</param>
    /// <param name="requestingUserId">
    /// The user requesting removal. Only the comment's author or a project Admin
    /// may remove a comment — enforced by policy in Phase 7. The domain only
    /// enforces that the comment exists and belongs to this task.
    /// </param>
    /// <param name="now">The date and time when the comment is being removed. Carried in the domain event.</param>
    /// <exception cref="DomainException">
    /// Thrown if the comment is not found on this task.
    /// </exception>
    public void RemoveComment(Guid commentId, Guid requestingUserId, DateTimeOffset now)
    {
        var comment = _comments.FirstOrDefault(c => c.Id == commentId)
            ?? throw new DomainException(
                "Comment not found on this task.",
                "task.comment-not-found"
            );

        comment.SoftDelete(now);

        RaiseDomainEvent(new CommentRemoved(
            Id,
            commentId,
            requestingUserId,
            now));
    }
    #endregion

    #region Attachments
    /// <summary>
    /// Records a new file attachment on the task.
    /// </summary>
    /// <param name="fileName">Original filename (e.g. "screenshot.png").</param>
    /// <param name="contentType">MIME type (e.g. "image/png").</param>
    /// <param name="sizeInBytes">File size in bytes.</param>
    /// <param name="storageUrl">
    /// URL or storage key pointing to the file in blob storage (S3, Azure Blob, etc.).
    /// The file itself is uploaded by the Application layer before this method is called;
    /// this entity only records the metadata.
    /// </param>
    /// <param name="uploadedByUserId">The user uploading the file.</param>
    /// <param name="now">The date and time when the attachment is being added. Carried in the domain event.</param>
    /// <returns>The newly created <see cref="Attachment"/>.</returns>
    /// <exception cref="DomainException">Thrown if the task is soft-deleted.</exception>
    public Attachment AddAttachment(
        string fileName,
        string contentType,
        long sizeInBytes,
        string storageUrl,
        Guid uploadedByUserId,
        DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new DomainException(
                "Cannot add an attachment to a deleted task.",
                "task.deleted"
            );
        }

        var attachment = Attachment.Create(
            Id,
            fileName,
            contentType,
            sizeInBytes,
            storageUrl,
            uploadedByUserId,
            now);
        _attachments.Add(attachment);

        RaiseDomainEvent(new AttachmentAdded(
            Id,
            attachment.Id,
            fileName,
            uploadedByUserId,
            now));

        return attachment;
    }

    /// <summary>
    /// Removes an attachment from the task.
    /// </summary>
    /// <param name="attachmentId">The Id of the attachment to remove.</param>
    /// <param name="removedByUserId">The user performing the removal.</param>
    /// <param name="now">The date and time when the attachment is being removed. Carried in the domain event.</param>
    /// <exception cref="DomainException">Thrown if the attachment is not found on this task.</exception>
    /// <remarks>
    /// This method removes the metadata record only. The Application layer is
    /// responsible for also deleting the file from blob storage after calling this.
    /// </remarks>
    public void RemoveAttachment(Guid attachmentId, Guid removedByUserId, DateTimeOffset now)
    {
        Attachment attachment = _attachments.FirstOrDefault(a => a.Id == attachmentId)
            ?? throw new DomainException(
                "Attachment not found on this task.",
                "attachment.not-found"
            );

        _attachments.Remove(attachment);

        RaiseDomainEvent(new AttachmentRemoved(
            Id,
            attachmentId,
            removedByUserId,
            now));
    }
    #endregion

    #region Guards
    /// <summary>
    /// Asserts the task is not in a terminal state.
    /// Called at the start of any mutation that should be blocked once
    /// a task is <see cref="ProjectTaskStatus.Done"/> or <see cref="ProjectTaskStatus.Cancelled"/>.
    /// </summary>
    /// <exception cref="DomainException">Thrown if the task is in a terminal state.</exception>
    private void EnsureNotTerminal()
    {
        if (Status.IsTerminal())
        {
            throw new DomainException(
                $"Task is in a terminal state('{Status}') and cannot be modified.",
                "task.terminal-state");
        }
    }
    #endregion
}
