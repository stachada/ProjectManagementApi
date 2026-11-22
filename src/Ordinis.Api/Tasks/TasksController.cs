using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ordinis.Api.Common;
using Ordinis.Api.Common.DataShaping;
using Ordinis.Api.Tasks.Requests;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Tasks;

/// <summary>
/// Manages tasks: creation, lookup, updates, deletion, and comments/attachments on a task.
/// </summary>
/// <remarks>
/// The acting user on every command is the authenticated caller from
/// <see cref="ICurrentUserService"/> (the access token's <c>sub</c> claim) - never a value the
/// client supplies in the body, query string, or form, which would let a caller act as anyone.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/tasks")]
public sealed class TasksController(IDispatcher dispatcher, ICurrentUserService currentUser) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly ICurrentUserService _currentUser = currentUser;

    /// <summary>
    /// Lists tasks, with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="boardId">Optional: restrict to this board.</param>
    /// <param name="assigneeId">Optional: restrict to tasks assigned to this user.</param>
    /// <param name="status">Optional: restrict to this status.</param>
    /// <param name="priority">Optional: restrict to this priority.</param>
    /// <param name="dueBefore">Optional: restrict to tasks due on or before this instant.</param>
    /// <param name="dueAfter">Optional: restrict to tasks due on or after this instant.</param>
    /// <param name="sortBy">Sort field: <c>title</c>, <c>priority</c>, <c>duedate</c>, or <c>createdAt</c> (default).</param>
    /// <param name="sortDescending">Sort in descending order. Defaults to <see langword="false"/>.</param>
    /// <param name="page">1-based page number. Defaults to 1.</param>
    /// <param name="pageSize">Items per page (server clamps to 1-100). Defaults to 20.</param>
    /// <param name="fields">
    /// Optional comma-separated list of field names to include in each returned object (sparse
    /// fieldset). <c>Id</c> is always included. Omit to return every field.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">
    /// Returns the page of tasks. Sets the <c>X-Total-Count</c> response header to the total
    /// number of matching tasks across all pages.
    /// </response>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTasks(
        [FromQuery] Guid? boardId = null,
        [FromQuery] Guid? assigneeId = null,
        [FromQuery] ProjectTaskStatus? status = null,
        [FromQuery] Priority? priority = null,
        [FromQuery] DateTimeOffset? dueBefore = null,
        [FromQuery] DateTimeOffset? dueAfter = null,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] bool sortDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? fields = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new TaskFilter(
            BoardId: boardId,
            AssigneeId: assigneeId,
            Status: status,
            Priority: priority,
            DueBefore: dueBefore,
            DueAfter: dueAfter,
            Page: page,
            PageSize: pageSize,
            SortBy: sortBy,
            SortDescending: sortDescending);
        var query = new GetTasksFiltered(filter);

        PagedResult<TaskSummaryDto> result = await _dispatcher.QueryAsync<GetTasksFiltered, PagedResult<TaskSummaryDto>>(query, cancellationToken);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());

        return Ok(DataShaper.ShapeCollection(result.Items, fields));
    }

    /// <summary>
    /// Retrieves a single task by its ID, including its comments and attachments.
    /// </summary>
    /// <param name="id">The task's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">
    /// The task was found and is returned. Cacheable for 30 seconds
    /// (<c>Cache-Control: private, max-age=30</c>; client-side only, since responses are per caller).
    /// </response>
    /// <response code="404">No task exists with the given ID.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "TaskMember")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByHeader = "Accept-Encoding,Authorization")]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        if (!string.IsNullOrEmpty(task.ConcurrencyToken))
        {
            Response.Headers.ETag = $"\"{task.ConcurrencyToken}\"";
        }

        return Ok(task);
    }

    /// <summary>
    /// Lists a task's comments.
    /// </summary>
    /// <param name="id">The task whose comments to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns all comments on the task.</response>
    /// <response code="404">No task exists with the given ID.</response>
    [HttpGet("{id:guid}/comments")]
    [Authorize(Policy = "TaskMember")]
    [ProducesResponseType(typeof(IReadOnlyCollection<CommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyCollection<CommentDto>>> GetComments(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        return Ok(task.Comments);
    }

    /// <summary>
    /// Lists a task's attachments.
    /// </summary>
    /// <param name="id">The task whose attachments to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns all attachments on the task.</response>
    /// <response code="404">No task exists with the given ID.</response>
    [HttpGet("{id:guid}/attachments")]
    [Authorize(Policy = "TaskMember")]
    [ProducesResponseType(typeof(IReadOnlyCollection<AttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyCollection<AttachmentDto>>> GetAttachments(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        return Ok(task.Attachments);
    }

    /// <summary>
    /// Creates a new task on a board.
    /// </summary>
    /// <param name="request">The board, title, and other task details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The task was created. The <c>Location</c> header points at <see cref="GetById"/>.</response>
    /// <response code="422">The request failed validation (e.g. board does not exist or is archived, assignee not found).</response>
    [HttpPost]
    [Authorize]
    [Idempotent]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TaskDto>> Create(
        [FromBody] CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateTask(
            BoardId: request.BoardId,
            Title: request.Title,
            Description: request.Description,
            Priority: request.Priority,
            AssigneeId: request.AssigneeId,
            DueDate: request.DueDate,
            RequestedByUserId: _currentUser.UserId);

        Guid id = await _dispatcher.SendAsync<CreateTask, Guid>(command, cancellationToken);

        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, task);
    }

    /// <summary>
    /// Updates a task's title, description, priority, and due date.
    /// Status and assignment changes go through the dedicated <see cref="Move"/>,
    /// <see cref="Assign"/>, <see cref="Unassign"/>, <see cref="Close"/>, and
    /// <see cref="Reopen"/> endpoints instead.
    /// </summary>
    /// <param name="id">The task to update.</param>
    /// <param name="request">The new task details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was updated.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "TaskOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateTask(
            TaskId: id,
            Title: request.Title,
            Description: request.Description,
            Priority: request.Priority,
            DueDate: request.DueDate,
            RequestedByUserId: _currentUser.UserId,
            IfMatch: HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Soft-deletes a task, removing it from normal query results.
    /// </summary>
    /// <param name="id">The task to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was deleted.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The <c>If-Match</c> header is missing.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "TaskProjectAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var command = new DeleteTask(id, _currentUser.UserId, HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);
        await _dispatcher.SendAsync(command, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Transitions a task to a new workflow status.
    /// </summary>
    /// <param name="id">The task to transition.</param>
    /// <param name="request">The target status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was transitioned.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The transition is not legal from the task's current status.</response>
    [HttpPost("{id:guid}/move")]
    [Authorize(Policy = "TaskOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Move(
        Guid id,
        [FromBody] MoveTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new MoveTask(
            TaskId: id,
            NewStatus: request.Status,
            RequestedByUserId: _currentUser.UserId,
            IfMatch: HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Assigns a task to a user, or reassigns it from the current assignee.
    /// </summary>
    /// <param name="id">The task to assign.</param>
    /// <param name="request">The target assignee.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was assigned.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation (e.g. assignee does not exist, task is in a terminal state, or already assigned to this user).</response>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "TaskOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Assign(
        Guid id,
        [FromBody] AssignTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new AssignTask(
            TaskId: id,
            AssigneeId: request.AssigneeId,
            RequestedByUserId: _currentUser.UserId,
            IfMatch: HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Removes the current assignee from a task, leaving it unassigned.
    /// </summary>
    /// <param name="id">The task to unassign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was unassigned.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The task is in a terminal state or already unassigned.</response>
    [HttpPost("{id:guid}/unassign")]
    [Authorize(Policy = "TaskOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Unassign(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new UnassignTask(id, _currentUser.UserId, HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);
        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Convenience alias for <see cref="Move"/> that transitions the task to
    /// <see cref="ProjectTaskStatus.Done"/>.
    /// </summary>
    /// <param name="id">The task to close.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was closed.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">
    /// The task cannot move to <see cref="ProjectTaskStatusExtensions.CanTransitionTo"/>
    /// <see cref="ProjectTaskStatus.Done"/> from its current status - only <see cref="ProjectTaskStatus.InReview"/> allows it.
    /// </response>
    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = "TaskOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Close(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new MoveTask(
            TaskId: id,
            NewStatus: ProjectTaskStatus.Done,
            RequestedByUserId: _currentUser.UserId,
            IfMatch: HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reopens a <see cref="ProjectTaskStatus.Done"/> task, moving it back to
    /// <see cref="ProjectTaskStatus.ToDo"/>. Unlike <see cref="Move"/> to ToDo, it is rejected for
    /// any task that is not Done.
    /// </summary>
    /// <param name="id">The task to reopen.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was reopened.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">
    /// The task is not currently <see cref="ProjectTaskStatus.Done"/> - reopening is the only
    /// transition <see cref="ProjectTaskStatus.Done"/> permits, and it is not reachable from
    /// any other status.
    /// </response>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = "TaskOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reopen(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new ReopenTask(
            TaskId: id,
            RequestedByUserId: _currentUser.UserId,
            IfMatch: HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[]);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Adds a comment to a task.
    /// </summary>
    /// <param name="id">The task to comment on.</param>
    /// <param name="request">The comment content. The author is the authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The comment was added. The <c>Location</c> header points at <see cref="GetComments"/>.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="422">The request failed validation (e.g. empty content).</response>
    [HttpPost("{id:guid}/comments")]
    [Authorize(Policy = "TaskContributor")]
    [Idempotent]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CommentDto>> AddComment(
        Guid id,
        [FromBody] AddCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new AddComment(
            TaskId: id,
            AuthorId: _currentUser.UserId,
            Content: request.Content);

        Guid commentId = await _dispatcher.SendAsync<AddComment, Guid>(command, cancellationToken);

        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        CommentDto comment = task.Comments.First(c => c.Id == commentId);

        return CreatedAtAction(nameof(GetComments), new { id }, comment);
    }

    /// <summary>
    /// Replaces the content of an existing comment. Only the original author may edit it.
    /// </summary>
    /// <param name="id">The task that owns the comment.</param>
    /// <param name="commentId">The comment to edit.</param>
    /// <param name="request">The replacement content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The comment was updated.</response>
    /// <response code="422">The request failed validation (e.g. task or comment does not exist, or requester is not the comment's author).</response>
    [HttpPut("{id:guid}/comments/{commentId:guid}")]
    [Authorize(Policy = "TaskContributor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EditComment(
        Guid id,
        Guid commentId,
        [FromBody] EditCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new EditComment(
            TaskId: id,
            CommentId: commentId,
            NewContent: request.NewContent,
            RequestedByUserId: _currentUser.UserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Removes a comment from a task. Only the comment's author, an Admin of the task's project,
    /// or an org Admin of the project's organization may remove it.
    /// </summary>
    /// <param name="id">The task that owns the comment.</param>
    /// <param name="commentId">The comment to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The comment was removed.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="422">
    /// The comment does not exist on this task (<c>task.comment-not-found</c>), or the caller is
    /// neither its author nor an admin (<c>task.comment-not-author</c>).
    /// </response>
    [HttpDelete("{id:guid}/comments/{commentId:guid}")]
    [Authorize(Policy = "TaskContributor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveComment(
        Guid id,
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        await _dispatcher.SendAsync(new RemoveComment(id, commentId, _currentUser.UserId), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Uploads a file and attaches it to a task.
    /// </summary>
    /// <param name="id">The task to attach the file to.</param>
    /// <param name="file">The file to upload. The uploader is the authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The attachment was added. The <c>Location</c> header points at <see cref="GetAttachments"/>.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="422">The request failed validation (e.g. empty file).</response>
    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = "TaskContributor")]
    [Consumes("multipart/form-data")]
    [Idempotent]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AttachmentDto>> AddAttachment(
        Guid id,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken = default)
    {
        await using Stream stream = file.OpenReadStream();

        var command = new AddAttachment(
            TaskId: id,
            FileName: file.FileName,
            ContentType: file.ContentType,
            SizeInBytes: file.Length,
            FileStream: stream,
            UploadedByUserId: _currentUser.UserId);

        Guid attachmentId = await _dispatcher.SendAsync<AddAttachment, Guid>(command, cancellationToken);

        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        AttachmentDto attachment = task.Attachments.First(a => a.Id == attachmentId);

        return CreatedAtAction(nameof(GetAttachments), new { id }, attachment);
    }

    /// <summary>
    /// Removes an attachment from a task, including its stored file.
    /// </summary>
    /// <param name="id">The task that owns the attachment.</param>
    /// <param name="attachmentId">The attachment to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The attachment was removed.</response>
    /// <response code="404">
    /// No task exists with the given ID, or no attachment exists with the given ID on this task
    /// (the handler checks attachment existence itself before delegating to the domain, unlike
    /// <see cref="RemoveComment"/> — so this is <c>404</c>, not <c>422</c>).
    /// </response>
    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = "TaskContributor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAttachment(
        Guid id,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await _dispatcher.SendAsync(new RemoveAttachment(id, attachmentId, _currentUser.UserId), cancellationToken);

        return NoContent();
    }
}
