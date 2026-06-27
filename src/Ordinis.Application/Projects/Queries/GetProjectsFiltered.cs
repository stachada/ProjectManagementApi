using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Queries;

/// <summary>
/// Filter and sort parameters for project list queries. All fields are optional.
/// </summary>
public sealed record ProjectFilter
{
    /// <summary>
    /// Restrict to projects belonging to this organization.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Restritct to projects where this user is a member.
    /// </summary>
    public Guid? MemberId { get; init; }

    /// <summary>
    /// When true, include archived projects. Defaults to false.
    /// </summary>
    public bool IncludeArchived { get; init; } = false;

    /// <summary>
    /// Field sort by. Supported values: <c>name</c>, <c>createdAt</c>.
    /// Defaults to <c>createdAt</c>.
    /// </summary>
    public string SortBy { get; init; } = "createdAt";

    /// <summary>
    /// When true, sort in descending order.
    /// </summary>
    public bool SortDescending { get; init; } = false;

    /// <summary>
    /// 1-based page number. Defaults to 1.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Items per page. Capped at 100. Defaults to 20.
    /// </summary>
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Returns a paginated, filterable list of projects.
/// </summary>
/// <param name="Filter">Filter and pagination parameters.</param>
public sealed record GetProjectsFiltered(ProjectFilter? Filter = null)
    : IQuery<PagedResult<ProjectSummaryDto>>;

/// <summary>
/// Handles <see cref="GetProjectsFiltered"/>.
/// Compose an <see cref="IQueryable{T}"/> chain from the filter parameters.
/// Member count and board count are projected directly from the EF Core query
/// to avoid loading full collections.
/// </summary>
/// <param name="db"></param>
public sealed class GetProjectsFilteredHandler(IAppDbContext db) : IQueryHandler<GetProjectsFiltered, PagedResult<ProjectSummaryDto>>
{
    public async Task<PagedResult<ProjectSummaryDto>> HandleAsync(
        GetProjectsFiltered query,
        CancellationToken cancellationToken = default)
    {
        ProjectFilter filter = query.Filter ?? new ProjectFilter();
        var pageSize = Math.Min(filter.PageSize, 100);
        var page = Math.Max(filter.Page, 1);

        IQueryable<Project> q = db.Projects;

        if (filter.OrganizationId.HasValue)
        {
            q = q.Where(p => p.OrganizationId == filter.OrganizationId.Value);
        }

        if (filter.MemberId.HasValue)
        {
            q = q.Where(p => p.Members.Any(m => m.UserId == filter.MemberId.Value));
        }

        if (!filter.IncludeArchived)
        {
            q = q.Where(p => !p.IsArchived);
        }

        q = (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("name", false) => q.OrderBy(p => p.Name),
            ("name", true) => q.OrderByDescending(p => p.Name),
            (_, false) => q.OrderBy(p => p.CreatedAt),
            (_, true) => q.OrderByDescending(p => p.CreatedAt)
        };

        var totalCount = await q.CountAsync(cancellationToken);

        // Project counts inline to avoid loading full collections.
        List<ProjectSummaryDto> items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProjectSummaryDto
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                Description = p.Description,
                IsArchived = p.IsArchived,
                OrganizationId = p.OrganizationId,
                CreatedByUserId = p.CreatedByUserId,
                MemberCount = p.Members.Count(),
                BoardCount = db.Boards.Count(b => b.ProjectId == p.Id),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ProjectSummaryDto>(items, totalCount, page, pageSize);
    }
}
