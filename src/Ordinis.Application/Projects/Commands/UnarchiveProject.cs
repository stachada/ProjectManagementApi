using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Restores an archived project to active status.
/// </summary>
/// <param name="ProjectId">The project to unarchive.</param>
public sealed record UnarchiveProject(Guid ProjectId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UnarchiveProject"/>.
/// </summary>
/// <param name="db"></param>
public sealed class UnarchiveProjectHandler(IAppDbContext db) : ICommandHandler<UnarchiveProject>
{
    public async Task HandleAsync(UnarchiveProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.Unarchive();
        await db.SaveChangesAsync(cancellationToken);
    }
}
