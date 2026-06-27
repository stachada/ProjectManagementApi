using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Queries;

// Query
/// <summary>
/// Returns the full detail view of a single project, including all boards
/// (capped at <see cref="ProjectDto.MaxEmbeddedCollectionSize"/>) and all
/// members with their display names.
/// </summary>
/// <param name="ProjectId">The project to retrieve.</param>
public sealed record GetProjectById(Guid ProjectId) : IQuery<ProjectDto>;

/// <summary>
/// Handles <see cref="GetProjectById"/>.
/// Loads the project with boards and members in a single query, then
/// resolves member display names via a batch user lookup.
/// </summary>
/// <param name="db"></param>
public sealed class GetProjectByIdHandler(IAppDbContext db) : IQueryHandler<GetProjectById, ProjectDto>
{
    public async Task<ProjectDto> HandleAsync(GetProjectById query, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.Id == query.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), query.ProjectId);

        // Batch-resolve all member display names in one query.
        var memberUserIds = project.Members.Select(m => m.UserId).ToHashSet();

        Dictionary<Guid, string> userLookup = await db.Users
            .Where(u => memberUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        // Board is an independent aggregate root - no navigation from Project,
        // so its boards are loaded with a direct query scoped by ProjectId.
        List<Board> boards = await db.Boards
            .Where(b => b.ProjectId == query.ProjectId)
            .ToListAsync(cancellationToken);

        var boardIds = boards.Select(b => b.Id).ToHashSet();

        Dictionary<Guid, int> boardTaskCounts = await db.Tasks
            .Where(t => boardIds.Contains(t.BoardId))
            .GroupBy(t => t.BoardId)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.BoardId, g => g.Count, cancellationToken);

        return project.ToDto(userLookup, boardTaskCounts, boards);
    }
}
