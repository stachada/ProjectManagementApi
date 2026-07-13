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
using Ordinis.Domain.Users;

namespace Ordinis.Api.Projects;

/// <summary>
/// Manages projects: creation, lookup, renaming/description updates,
/// suspension/reactivation, and listing a project's tasks and members.
/// </summary>
/// <param name="dispatcher"></param>
[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController(IDispatcher dispatcher) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;

    /// <summary>
    /// Lists projects, with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="organizationId">Optional: restrict to projects in this organization.</param>
    /// <param name="memberId">Optional: restrict to projects where this user is a member.</param>
    /// <param name="includeArchived">Include archived projects. Defaults to <see langword="false"/>.</param>
    /// <param name="sortBy">Sort field: <c>name</c> or <c>createdAt</c> (default).</param>
    /// <param name="sortDescending">Sort in descending order. Defaults to <see langword="false"/>.</param>
    /// <param name="page">1-based page number. Defaults to 1.</param>
    /// <param name="pageSize">Items per page (server clamps to 1-100). Defaults to 20.</param>
    /// <param name="fields">
    /// Optional comma-separated list of field names to include in each returned object (sparse
    /// fieldset). <c>Id</c> is always included. Omit to return every field.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">
    /// Returns the page of projects. Sets the <c>X-Total-Count</c> response header to the total
    /// number of matching projects across all pages.
    /// </response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjects(
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? memberId,
        [FromQuery] bool includeArchived = false,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] bool sortDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? fields = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new ProjectFilter
        {
            OrganizationId = organizationId,
            MemberId = memberId,
            IncludeArchived = includeArchived,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Page = page,
            PageSize = pageSize
        };
        var query = new GetProjectsFiltered(filter);

        PagedResult<ProjectSummaryDto> result = await _dispatcher.QueryAsync<GetProjectsFiltered, PagedResult<ProjectSummaryDto>>(
            query, cancellationToken);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());

        return Ok(DataShaper.ShapeCollection(result.Items, fields));
    }

    /// <summary>
    /// Retrieves a single project by its ID, including its boards and members.
    /// </summary>
    /// <param name="id">The project's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The project was found and is returned.</response>
    /// <response code="404">No project exists with the given ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        ProjectDto dto = await _dispatcher.QueryAsync<GetProjectById, ProjectDto>(
            new GetProjectById(id), cancellationToken);

        return Ok(dto);
    }

    /// <summary>
    /// Lists tasks across all boards in a project, with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="id">The project whose tasks to list.</param>
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
    /// <response code="404">No project exists with the given ID.</response>
    [HttpGet("{id:guid}/tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<TaskSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTasks(
        Guid id,
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
        var filter = new TaskFilter
        {
            BoardId = boardId,
            AssigneeId = assigneeId,
            Status = status,
            Priority = priority,
            DueBefore = dueBefore,
            DueAfter = dueAfter,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Page = page,
            PageSize = pageSize
        };
        var query = new GetProjectTasks(id, filter);

        PagedResult<TaskSummaryDto> result = await _dispatcher.QueryAsync<GetProjectTasks, PagedResult<TaskSummaryDto>>(
            query, cancellationToken);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());

        return Ok(DataShaper.ShapeCollection(result.Items, fields));
    }

    /// <summary>
    /// Lists all members of a project.
    /// </summary>
    /// <param name="id">The project whose members to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns all members, ordered by <c>JoinedAt</c> ascending.</response>
    /// <response code="404">No project exists with the given ID.</response>
    [HttpGet("{id:guid}/members")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> GetMembers(Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProjectMemberDto> members = await _dispatcher.QueryAsync<GetProjectMembers, IReadOnlyList<ProjectMemberDto>>(
            new GetProjectMembers(id), cancellationToken);

        return Ok(members);
    }

    /// <summary>
    /// Lists a project's boards.
    /// </summary>
    /// <param name="id">The project whose boards to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">
    /// Returns the project's boards, capped at <see cref="ProjectDto.MaxEmbeddedCollectionSize"/>.
    /// </response>
    /// <response code="404">No project exists with the given ID.</response>
    [HttpGet("{id:guid}/boards")]
    [ProducesResponseType(typeof(IReadOnlyList<BoardSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BoardSummaryDto>>> GetBoards(Guid id, CancellationToken cancellationToken)
    {
        ProjectDto dto = await _dispatcher.QueryAsync<GetProjectById, ProjectDto>(
            new GetProjectById(id), cancellationToken);

        return Ok(dto.Boards);
    }

    /// <summary>
    /// Creates a new project under the specified organization.
    /// </summary>
    /// <param name="request">The owning organization, creator, name, and optional description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The project was created. The <c>Location</c> header points at <see cref="GetById"/>.</response>
    /// <response code="422">The request failed validation (e.g. duplicate name in the organization).</response>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProjectDto>> CreateProject(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        Guid id = await _dispatcher.SendAsync<CreateProject, Guid>(new CreateProject(
            request.OrganizationId,
            request.CreatedByUserId,
            request.Name,
            request.Description), cancellationToken);

        ProjectDto dto = await _dispatcher.QueryAsync<GetProjectById, ProjectDto>(
            new GetProjectById(id), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, dto);
    }

    /// <summary>
    /// Updates a project's display name and description. The slug is immutable and not affected.
    /// </summary>
    /// <param name="id">The project to update.</param>
    /// <param name="request">The new name and description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The project was updated.</response>
    /// <response code="404">No project exists with the given ID.</response>
    /// <response code="409">The project was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new UpdateProject(id, request.NewName, request.NewDescription), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Soft-deletes a project, removing it from normal query results.
    /// </summary>
    /// <param name="id">The project to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The project was deleted.</response>
    /// <response code="404">No project exists with the given ID.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new DeleteProject(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Archives a project, making it read-only. Boards, tasks, and history remain accessible.
    /// </summary>
    /// <param name="id">The project to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The project was archived.</response>
    /// <response code="404">No project exists with the given ID.</response>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new ArchiveProject(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Restores an archived project to active status.
    /// </summary>
    /// <param name="id">The project to unarchive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The project was unarchived.</response>
    /// <response code="404">No project exists with the given ID.</response>
    [HttpPost("{id:guid}/unarchive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new UnarchiveProject(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Adds a user to a project with the specified role.
    /// </summary>
    /// <param name="id">The project to add the member to.</param>
    /// <param name="request">The user to add and the role to assign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The member was added. The <c>Location</c> header points at <see cref="GetMembers"/>.</response>
    /// <response code="422">The request failed validation (e.g. project does not exist, user not found, already a member).</response>
    [HttpPost("{id:guid}/members")]
    [ProducesResponseType(typeof(ProjectMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(
        Guid id,
        [FromBody] AddProjectMemberRequest request,
        CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new AddProjectMember(id, request.UserId, request.Role), cancellationToken);

        IReadOnlyList<ProjectMemberDto> members = await _dispatcher.QueryAsync<GetProjectMembers, IReadOnlyList<ProjectMemberDto>>(
            new GetProjectMembers(id), cancellationToken);

        ProjectMemberDto dto = members.Single(m => m.UserId == request.UserId);

        return CreatedAtAction(nameof(GetMembers), new { id }, dto);
    }

    /// <summary>
    /// Changes an existing project member's role.
    /// </summary>
    /// <param name="id">The project containing the member.</param>
    /// <param name="userId">The member whose role should change.</param>
    /// <param name="request">The new role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The member's role was changed.</response>
    /// <response code="404">No project or membership exists for the given IDs.</response>
    /// <response code="422">Not allowed (e.g. demoting the last Admin).</response>
    [HttpPut("{id:guid}/members/{userId:guid}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeMemberRole(
        Guid id,
        Guid userId,
        [FromBody] ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new ChangeMemberRole(id, userId, request.NewRole), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Removes a user from a project.
    /// </summary>
    /// <param name="id">The project to remove the member from.</param>
    /// <param name="userId">The user to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The member was removed.</response>
    /// <response code="404">No project or membership exists for the given IDs.</response>
    /// <response code="422">Not allowed (e.g. removing the last Admin).</response>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new RemoveProjectMember(id, userId), cancellationToken);
        return NoContent();
    }
}
