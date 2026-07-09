using Microsoft.AspNetCore.Mvc;
using Ordinis.Api.Organizations.Requests;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.Application.Organizations.Queries;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;

namespace Ordinis.Api.Organizations;

/// <summary>
/// Manages organizations: creation, lookup, renaming/description updates,
/// suspension/reactivation, and listing an organization's projects.
/// </summary>
[ApiController]
[Route("api/v1/organizations")]
public sealed class OrganizationsController(IDispatcher dispatcher) : ControllerBase
{
    private readonly IDispatcher _dispatcher = dispatcher;

    /// <summary>
    /// Retrieves a single organization by its ID.
    /// </summary>
    /// <param name="id">The organization's unique identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The organization was found and is returned.</response>
    /// <response code="404">No organization exists with the given ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        OrganizationDto dto = await _dispatcher.QueryAsync<GetOrganizationById, OrganizationDto>(
            new GetOrganizationById(id), cancellationToken);

        return Ok(dto);
    }

    /// <summary>
    /// Lists projects belonging to an organization, with optional filtering, sorting, and pagination.
    /// </summary>
    /// <param name="id">The organization whose projects to list.</param>
    /// <param name="memberId">Optional: restrict to projects where this user is a member.</param>
    /// <param name="includeArchived">Include archived projects. Defaults to <see langword="false"/>.</param>
    /// <param name="sortBy">Sort field: <c>name</c> or <c>createdAt</c> (default).</param>
    /// <param name="sortDescending">Sort in descending order. Defaults to <see langword="false"/>.</param>
    /// <param name="page">1-based page number. Defaults to 1.</param>
    /// <param name="pageSize">Items per page (server clamps to 1-100). Defaults to 20.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">
    /// Returns the page of projects. Sets the <c>X-Total-Count</c> response header to the total
    /// number of matching projects across all pages.
    /// </response>
    /// <response code="404">No organization exists with the given ID.</response>
    [HttpGet("{id:guid}/projects")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProjectSummaryDto>>> GetProjects(
        Guid id,
        [FromQuery] Guid? memberId,
        [FromQuery] bool includeArchived,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] bool sortDescending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new ProjectFilter
        {
            MemberId = memberId,
            IncludeArchived = includeArchived,
            SortBy = sortBy,
            SortDescending = sortDescending,
            Page = page,
            PageSize = pageSize,
        };

        PagedResult<ProjectSummaryDto> result = await _dispatcher.QueryAsync<GetOrganizationProjects, PagedResult<ProjectSummaryDto>>(
            new GetOrganizationProjects(id, filter), cancellationToken);

        Response.Headers.Append("X-Total-Count", result.TotalCount.ToString());

        return Ok(result.Items);
    }

    /// <summary>
    /// Creates a new active organization.
    /// </summary>
    /// <param name="request">The new organization's name and optional description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="201">The organization was created. The <c>Location</c> header points at <see cref="GetById"/>.</response>
    /// <response code="422">The request failed validation (e.g. duplicate name).</response>
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrganizationDto>> Create(
        [FromBody] CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        Guid id = await _dispatcher.SendAsync<CreateOrganization, Guid>(
            new CreateOrganization(request.Name, request.Description), cancellationToken);

        OrganizationDto dto = await _dispatcher.QueryAsync<GetOrganizationById, OrganizationDto>(
            new GetOrganizationById(id), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, dto);
    }

    /// <summary>
    /// Renames an organization and replaces its description.
    /// </summary>
    /// <param name="id">The organization to update.</param>
    /// <param name="request">The new name and description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The organization was updated.</response>
    /// <response code="404">No organization exists with the given ID.</response>
    /// <response code="409">The organization was concurrently modified (stale <c>RowVersion</c>).</response>
    /// <response code="422">The request failed validation.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new RenameOrganization(id, request.Name), cancellationToken);
        await _dispatcher.SendAsync(new UpdateOrganizationDescription(id, request.Description), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Suspends an active organization. While suspended, the organization rejects new projects
    /// and user invitations.
    /// </summary>
    /// <param name="id">The organization to suspend.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The organization was suspended.</response>
    /// <response code="404">No organization exists with the given ID.</response>
    /// <response code="422">The organization is already suspended (<c>organization.already-suspended</c>).</response>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new SuspendOrganization(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Reactivates a suspended organization.
    /// </summary>
    /// <param name="id">The organization to reactivate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">The organization was reactivated.</response>
    /// <response code="404">No organization exists with the given ID.</response>
    /// <response code="422">The organization is already active (<c>organization.already-active</c>).</response>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await _dispatcher.SendAsync(new ReactivateOrganization(id), cancellationToken);
        return NoContent();
    }
}
