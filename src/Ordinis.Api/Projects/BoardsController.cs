using Microsoft.AspNetCore.Mvc;
using Ordinis.Api.Common.DataShaping;
using Ordinis.Api.Projects.Requests;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Projects;

/// <summary>
/// Manages boards: creation, lookup, renaming, archiving/unarchiving, and listing a board's tasks.
/// </summary>
[ApiController]
[Route("api/v1/boards")]
public sealed class BoardsController(IDispatcher dispatcher) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;

    /// <summary>
    /// Retrieves a single board by its ID.
    /// </summary>
    /// <param name="id">The board's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The board was found and is returned.</response>
    /// <response code="404">No board exists with the given ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BoardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BoardDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        BoardDto dto = await _dispatcher.QueryAsync<GetBoardById, BoardDto>(new GetBoardById(id), cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// Lists tasks on a board, with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="id">The board whose tasks to list.</param>
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
    /// <response code="404">No board exists with the given ID.</response>
    [HttpGet("{id:guid}/tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTasks(
        Guid id,
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
            BoardId: id,
            AssigneeId: assigneeId,
            Status: status,
            Priority: priority,
            DueBefore: dueBefore,
            DueAfter: dueAfter,
            Page: page,
            PageSize: pageSize,
            SortBy: sortBy,
            SortDescending: sortDescending);
        var query = new GetBoardTasks(id, filter);

        PagedResult<TaskSummaryDto> result = await _dispatcher.QueryAsync<GetBoardTasks, PagedResult<TaskSummaryDto>>(
            query, cancellationToken);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());

        return Ok(DataShaper.ShapeCollection(result.Items, fields));
    }

    /// <summary>
    /// Creates a new board within a project.
    /// </summary>
    /// <param name="projectId">The project to add the board to.</param>
    /// <param name="request">The creator and the new board's name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The board was created. The <c>Location</c> header points at <see cref="GetById"/>.</response>
    /// <response code="422">The request failed validation (e.g. project does not exist or is archived, duplicate board name).</response>
    [HttpPost("/api/v1/projects/{projectId:guid}/boards")]
    [ProducesResponseType(typeof(BoardDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BoardDto>> Create(
        Guid projectId,
        [FromBody] CreateBoardRequest request,
        CancellationToken cancellationToken)
    {
        Guid id = await _dispatcher.SendAsync<CreateBoard, Guid>(
            new CreateBoard(projectId, request.CreatedByUserId, request.Name), cancellationToken);

        BoardDto dto = await _dispatcher.QueryAsync<GetBoardById, BoardDto>(new GetBoardById(id), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, dto);
    }

    /// <summary>
    /// Renames a board. The new name must be unique within the project (case-insensitive).
    /// </summary>
    /// <param name="id">The board to rename.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The board was renamed.</response>
    /// <response code="404">No board exists with the given ID.</response>
    /// <response code="409">The board was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation (e.g. duplicate name, board archived).</response>
    [HttpPut("{id:guid}/name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RenameBoard(
        Guid id,
        [FromBody] RenameBoardRequest request,
        CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new RenameBoard(id, request.NewName), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Archives a board, making it read-only. Tasks remain accessible for audit purposes.
    /// </summary>
    /// <param name="id">The board to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The board was archived.</response>
    /// <response code="404">No board exists with the given ID.</response>
    /// <response code="409">The board was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The board is already archived (<c>board.already-archived</c>).</response>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ArchiveBoard(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new ArchiveBoard(id), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Restores an archived board to active status.
    /// </summary>
    /// <param name="id">The board to unarchive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The board was unarchived.</response>
    /// <response code="404">No board exists with the given ID.</response>
    /// <response code="409">The board was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The board is not archived (<c>board.not-archived</c>).</response>
    [HttpPost("{id:guid}/unarchive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UnarchiveBoard(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new UnarchiveBoard(id), cancellationToken);

        return NoContent();
    }
}
