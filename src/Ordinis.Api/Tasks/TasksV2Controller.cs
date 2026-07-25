using Microsoft.AspNetCore.Mvc;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;

namespace Ordinis.Api.Tasks;

/// <summary>
/// Demonstrates URL-segment API versioning: a v2 task lookup returning the same
/// <see cref="TaskDto"/> as <c>api/v1/tasks/{id}</c>, plus a <c>board</c> hypermedia link
/// pointing at the task's parent board that v1 does not include.
/// </summary>
[ApiController]
[Route("api/v2/tasks")]
public sealed class TasksV2Controller(IDispatcher dispatcher) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;

    /// <summary>
    /// Gets a task by ID, including a <c>board</c> link to its parent board.
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
        TaskDto original = await _dispatcher.QueryAsync<GetTaskById, TaskDto>(query, cancellationToken);

        var boardLink = new HateoasLink("board", $"/api/v1/boards/{original.BoardId}", "GET");
        TaskDto task = new()
        {
            Id = original.Id,
            BoardId = original.BoardId,
            Title = original.Title,
            Description = original.Description,
            Status = original.Status,
            Priority = original.Priority,
            AssigneeId = original.AssigneeId,
            AssigneeDisplayName = original.AssigneeDisplayName,
            DueDate = original.DueDate,
            CreatedAt = original.CreatedAt,
            UpdatedAt = original.UpdatedAt,
            ConcurrencyToken = original.ConcurrencyToken,
            Comments = original.Comments,
            Attachments = original.Attachments,
            Links = [.. original.Links, boardLink],
        };

        if (!string.IsNullOrEmpty(task.ConcurrencyToken))
        {
            Response.Headers.ETag = $"\"{task.ConcurrencyToken}\"";
        }

        return Ok(task);
    }
}
