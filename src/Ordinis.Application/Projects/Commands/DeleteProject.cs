using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Soft-deletes a project, removing it from normal query results.
/// Distinct from archiving - soft-deleted projects cannot be restored
/// through the API. Prefer <see cref="ArchiveProject"/> for active
/// projects with existing task history.
/// </summary>
/// <param name="ProjectId">The project to soft-delete.</param>
public sealed record DeleteProject(Guid ProjectId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="DeleteProject"/>.
/// </summary>
public sealed class DeleteProjectHandler(IAppDbContext db, TimeProvider timeProvider) : ICommandHandler<DeleteProject>
{
    public async Task HandleAsync(DeleteProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        var now = timeProvider.GetUtcNow();
        project.SoftDelete(now);
        await db.SaveChangesAsync(cancellationToken);
    }
}
