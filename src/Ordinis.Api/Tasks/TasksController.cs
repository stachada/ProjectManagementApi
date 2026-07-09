using Microsoft.AspNetCore.Mvc;
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
[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController(IDispatcher dispatcher) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;

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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">
    /// Returns the page of tasks. Sets the <c>X-Total-Count</c> response header to the total
    /// number of matching tasks across all pages.
    /// </response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TaskSummaryDto>>> GetTasks(
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

        return Ok(result.Items);
    }

    /// <summary>
    /// Retrieves a single task by its ID, including its comments and attachments.
    /// </summary>
    /// <param name="id">The task's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The task was found and is returned.</response>
    /// <response code="404">No task exists with the given ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

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
    /// <response code="404">No board exists with the given ID.</response>
    /// <response code="422">The request failed validation (e.g. board archived, assignee not found).</response>
    [HttpPost]
    [ProducesResponseType(typeof(TaskDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
            RequestedByUserId: request.RequestedByUserId);

        Guid id = await _dispatcher.SendAsync<CreateTask, Guid>(command, cancellationToken);

        var query = new GetTaskById(id);
        TaskDto task = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, task);
    }

    /// <summary>
    /// Updates a task's title, description, priority, and due date.
    /// Status and assignment changes go through dedicated endpoints (Phase 7).
    /// </summary>
    /// <param name="id">The task to update.</param>
    /// <param name="request">The new task details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was updated.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="409">The task was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation.</response>
    [HttpPut("{id:guid}")]
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
            RequestedByUserId: request.RequestedByUserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Soft-deletes a task, removing it from normal query results.
    /// </summary>
    /// <param name="id">The task to delete.</param>
    /// <param name="requestedByUserId">The user performing the deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The task was deleted.</response>
    /// <response code="404">No task exists with the given ID.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new DeleteTask(id, requestedByUserId), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Adds a comment to a task.
    /// </summary>
    /// <param name="id">The task to comment on.</param>
    /// <param name="request">The comment author and content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The comment was added. The <c>Location</c> header points at <see cref="GetComments"/>.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="422">The request failed validation (e.g. empty content).</response>
    [HttpPost("{id:guid}/comments")]
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
            AuthorId: request.AuthorId,
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
    /// <param name="request">The replacement content and the requesting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The comment was updated.</response>
    /// <response code="404">No task or comment exists for the given IDs.</response>
    /// <response code="422">The request failed validation (e.g. requester is not the comment's author).</response>
    [HttpPut("{id:guid}/comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
            RequestedByUserId: request.RequestedByUserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Removes a comment from a task.
    /// </summary>
    /// <param name="id">The task that owns the comment.</param>
    /// <param name="commentId">The comment to remove.</param>
    /// <param name="requestedByUserId">The user performing the removal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The comment was removed.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="422">The comment does not exist on this task (<c>task.comment-not-found</c>).</response>
    [HttpDelete("{id:guid}/comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveComment(
        Guid id,
        Guid commentId,
        [FromQuery] Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        await _dispatcher.SendAsync(new RemoveComment(id, commentId, requestedByUserId), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Uploads a file and attaches it to a task.
    /// </summary>
    /// <param name="id">The task to attach the file to.</param>
    /// <param name="file">The file to upload.</param>
    /// <param name="uploadedByUserId">The user uploading the attachment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The attachment was added. The <c>Location</c> header points at <see cref="GetAttachments"/>.</response>
    /// <response code="404">No task exists with the given ID.</response>
    /// <response code="422">The request failed validation (e.g. empty file).</response>
    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AttachmentDto>> AddAttachment(
        Guid id,
        [FromForm] IFormFile file,
        [FromForm] Guid uploadedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using Stream stream = file.OpenReadStream();

        var command = new AddAttachment(
            TaskId: id,
            FileName: file.FileName,
            ContentType: file.ContentType,
            SizeInBytes: file.Length,
            FileStream: stream,
            UploadedByUserId: uploadedByUserId);

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
    /// <param name="removedByUserId">The user performing the removal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The attachment was removed.</response>
    /// <response code="404">
    /// No task exists with the given ID, or no attachment exists with the given ID on this task
    /// (the handler checks attachment existence itself before delegating to the domain, unlike
    /// <see cref="RemoveComment"/> — so this is <c>404</c>, not <c>422</c>).
    /// </response>
    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAttachment(
        Guid id,
        Guid attachmentId,
        [FromQuery] Guid removedByUserId,
        CancellationToken cancellationToken = default)
    {
        await _dispatcher.SendAsync(new RemoveAttachment(id, attachmentId, removedByUserId), cancellationToken);

        return NoContent();
    }
}
