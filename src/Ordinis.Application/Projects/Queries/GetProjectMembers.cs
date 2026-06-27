using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Queries;

// Query
/// <summary>
/// Returns all members of a project with their display names.
/// Ordered by <c>JoinedAt</c> ascending.
/// </summary>
/// <param name="ProjectId">The project whose members to retrieve.</param>
public sealed record GetProjectMembers(Guid ProjectId) : IQuery<IReadOnlyList<ProjectMemberDto>>;

// Handler
/// <summary>
/// Handles <see cref="GetProjectMembers"/>.
/// Projects member data directly from the DB with a batch user name lookup.
/// without loading the full <see cref="Project"/> aggregate.
/// </summary>
/// <param name="db"></param>
public sealed class GetProjectMembersHandler(IAppDbContext db)
    : IQueryHandler<GetProjectMembers, IReadOnlyList<ProjectMemberDto>>
{
    public async Task<IReadOnlyList<ProjectMemberDto>> HandleAsync(
        GetProjectMembers query,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects
            .AnyAsync(p => p.Id == query.ProjectId, cancellationToken);

        if (!projectExists)
        {
            throw new NotFoundException(nameof(Domain.Projects.Project), query.ProjectId);
        }

        List<ProjectMember> members = await db.ProjectMembers
            .Where(m => m.ProjectId == query.ProjectId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        var userIds = members.Select(m => m.UserId).ToHashSet();

        Dictionary<Guid, string> userLookup = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return members
            .Select(m => m.ToMemberDto(userLookup))
            .ToList();
    }
}
