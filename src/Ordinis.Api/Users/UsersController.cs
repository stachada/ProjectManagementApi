using Microsoft.AspNetCore.Mvc;
using Ordinis.Api.Common.DataShaping;
using Ordinis.Api.Users.Requests;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Tasks.Queries;
using Ordinis.Application.Users.Commands;
using Ordinis.Application.Users.Dtos;
using Ordinis.Application.Users.Queries;
using Ordinis.Domain.Tasks;

namespace Ordinis.Api.Users;

/// <summary>
/// Manages users: creation, lookup, profile updates, organization-role changes,
/// deactivation/reactivation, and listing a user's assigned tasks.
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(IDispatcher dispatcher) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;

    /// <summary>
    /// Retrieves a single user by ID.
    /// </summary>
    /// <param name="id">The ID of the user to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The user was found and is returned.</response>
    /// <response code="404">No user exists with the given ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        UserDto dto = await _dispatcher.QueryAsync<GetUserById, UserDto>(new GetUserById(id), cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// Lists tasks assigned to a specific user, with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="id">The user whose tasks to list.</param>
    /// <param name="boardId">Optional: restrict to this board.</param>
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
    /// <response code="404">No user exists with the given ID.</response>
    [HttpGet("{id:guid}/tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTasks(
        Guid id,
        [FromQuery] Guid? boardId = null,
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
            AssigneeId: null, // ignored, always overridden with user ID
            Status: status,
            Priority: priority,
            DueBefore: dueBefore,
            DueAfter: dueAfter,
            SortBy: sortBy,
            SortDescending: sortDescending,
            Page: page,
            PageSize: pageSize);

        var query = new GetUserTasks(id, filter);
        PagedResult<TaskSummaryDto> result = await _dispatcher.QueryAsync<GetUserTasks, PagedResult<TaskSummaryDto>>(
            query, cancellationToken);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());

        return Ok(DataShaper.ShapeCollection(result.Items, fields));
    }

    /// <summary>
    /// Creates a new user in the specified organization.
    /// </summary>
    /// <param name="request">The owning organization and the new user's details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The user was created. The <c>Location</c> header points at <see cref="GetById"/>.</response>
    /// <response code="422">The request failed validation (e.g. duplicate email in the organization).</response>
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateUser(
            OrganizationId: request.OrganizationId,
            DisplayName: request.DisplayName,
            Email: request.Email,
            Password: request.Password,
            OrgRole: request.OrgRole);

        Guid id = await _dispatcher.SendAsync<CreateUser, Guid>(command, cancellationToken);

        UserDto dto = await _dispatcher.QueryAsync<GetUserById, UserDto>(new GetUserById(id), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, dto);
    }

    /// <summary>
    /// Updates a user's display name. Only the user themselves or an Admin may perform this action.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <param name="request">The new display name and the ID of the user making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The user's display name was updated.</response>
    /// <response code="404">No user exists with the given ID.</response>
    /// <response code="409">The user was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateUser(
            UserId: id,
            DisplayName: request.DisplayName,
            RequestedByUserId: request.RequestedByUserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Changes the organization-level role of a user. Only Admins may perform this action.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <param name="request">The new organization role and the ID of the user making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The user's role was changed.</response>
    /// <response code="404">No user exists with the given ID.</response>
    /// <response code="409">The user was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation.</response>
    [HttpPut("{id:guid}/org-role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeOrgRole(
        Guid id,
        [FromBody] ChangeUserOrgRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new ChangeUserOrgRole(
            UserId: id,
            NewOrgRole: request.NewOrgRole,
            RequestedByUserId: request.RequestedByUserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deactivates a user account. Only Admins may perform this action.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <param name="request">The ID of the user making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The user was deactivated.</response>
    /// <response code="404">No user exists with the given ID.</response>
    /// <response code="409">The user was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The user is already inactive (<c>user.already-inactive</c>).</response>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeactivateUser(
        Guid id,
        [FromBody] DeactivateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new DeactivateUser(
            UserId: id,
            RequestedByUserId: request.RequestedByUserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reactivates a previously deactivated user account. Only Admins may perform this action.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <param name="request">The ID of the user making the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The user was reactivated.</response>
    /// <response code="404">No user exists with the given ID.</response>
    /// <response code="409">The user was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The user is already active (<c>user.already-active</c>).</response>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReactivateUser(
        Guid id,
        [FromBody] ReactivateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new ReactivateUser(
            UserId: id,
            RequestedByUserId: request.RequestedByUserId);

        await _dispatcher.SendAsync(command, cancellationToken);

        return NoContent();
    }
}
