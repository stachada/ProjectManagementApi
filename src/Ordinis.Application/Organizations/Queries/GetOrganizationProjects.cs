using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Organizations.Queries;

// Query
/// <summary>
/// Returns a paginated, filtered list of projects belonging to an organization.
/// Reuses <see cref="ProjectFilter"/> with <c>OrganizationId</c> pre-set,
/// keeping filter/sort/pagination consistent with <c>GetProjectsFiltered</c>.
/// </summary>
/// <param name="OrganizationId">The organization whose projects to list.</param>
/// <param name="Filter">Optional filter, sort, and pagination parameters.</param>
public sealed record GetOrganizationProjects(
    Guid OrganizationId,
    ProjectFilter? Filter = null) : IQuery<PagedResult<ProjectSummaryDto>>;

// Handler
/// <summary>
/// Handles <see cref="GetOrganizationProjects"/>.
/// Applies the same EF Core LINQ composition pattern as
/// <c>GetProjectsFilteredHandler</c> but with <c>OrganizationId</c>
/// fixed from the route — avoids handler-to-handler coupling while
/// sharing zero duplicated query logic (the filter shape is identical).
/// </summary>
public sealed class GetOrganizationProjectsHandler(IAppDbContext db) : IQueryHandler<GetOrganizationProjects, PagedResult<ProjectSummaryDto>>
{
    public async Task<PagedResult<ProjectSummaryDto>> HandleAsync(
        GetOrganizationProjects query,
        CancellationToken cancellationToken = default)
    {
        // Verify the organization exists before querying its projects.
        var orgExists = await db.Organizations
            .AnyAsync(o => o.Id == query.OrganizationId, cancellationToken);

        if (!orgExists)
        {
            throw new NotFoundException(nameof(Organization), query.OrganizationId);
        }

        ProjectFilter filter = query.Filter ?? new ProjectFilter();
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        // Base query - OrganizationId is always fixed from the route.
        // Members is included so ProjectMapper.ToSummaryDto can read Members.Count.
        IQueryable<Project> q = db.Projects
            .Include(p => p.Members)
            .Where(p => p.OrganizationId == query.OrganizationId);

        // Optional filters
        if (!filter.IncludeArchived)
        {
            q = q.Where(p => !p.IsArchived);
        }

        if (filter.MemberId.HasValue)
        {
            q = q.Where(p => p.Members.Any(m => m.UserId == filter.MemberId.Value));
        }

        // Sorting
        q = (filter.SortBy.ToLowerInvariant(), filter.SortDescending) switch
        {
            ("name", false) => q.OrderBy(p => p.Name),
            ("name", true) => q.OrderByDescending(p => p.Name),
            (_, false) => q.OrderBy(p => p.CreatedAt),
            (_, true) => q.OrderByDescending(p => p.CreatedAt)
        };

        var totalCount = await q.CountAsync(cancellationToken);

        List<Project> projects = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Board is an independent aggregate root - no navigation from Project,
        // so board counts for this page are resolved via a separate grouped query.
        var projectIds = projects.Select(p => p.Id).ToList();

        Dictionary<Guid, int> boardCounts = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .GroupBy(b => b.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ProjectId, g => g.Count, cancellationToken);

        var items = projects
            .Select(p => p.ToSummaryDto(boardCounts.GetValueOrDefault(p.Id)))
            .ToList();

        return new PagedResult<ProjectSummaryDto>(items, totalCount, page, pageSize);
    }
}
