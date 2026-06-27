using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Queries;

// Query
/// <summary>
/// Returns the full detail view of a single organization.
/// </summary>
/// <param name="OrganizationId">The organization to retrieve.</param>
public sealed record GetOrganizationById(Guid OrganizationId) : IQuery<OrganizationDto>;

// Handler
/// <summary>
/// Handles <see cref="GetOrganizationById"/>.
/// Loads the organization and resolves the project count via a separate
/// scalar query - avoids loading a navigation collection across the
/// Organization -> Project aggregate boundary.
/// </summary>
/// <param name="db"></param>
public sealed class GetOrganizationByIdHandler(IAppDbContext db) : IQueryHandler<GetOrganizationById, OrganizationDto>
{
    public async Task<OrganizationDto> HandleAsync(
        GetOrganizationById query,
        CancellationToken cancellationToken = default)
    {
        Organization organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Id == query.OrganizationId, cancellationToken)
                ?? throw new NotFoundException(nameof(Organization), query.OrganizationId);

        var projectCount = await db.Projects
            .CountAsync(p => p.OrganizationId == query.OrganizationId, cancellationToken);

        return organization.ToDto(projectCount);
    }
}
