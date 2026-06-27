using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Archives a project, making it read-only. Boards, tasks and history
/// remain accessible for audit purposes. Reversible via <c>UnarchiveProject</c>.
/// </summary>
/// <param name="ProjectId">The project to archive.</param>
public sealed record ArchiveProject(Guid ProjectId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="ArchiveProject"/>.
/// </summary>
/// <param name="db"></param>
public sealed class ArchiveProjectHandler(IAppDbContext db) : ICommandHandler<ArchiveProject>
{
    public async Task HandleAsync(ArchiveProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.Archive();
        await db.SaveChangesAsync(cancellationToken);
    }
}
