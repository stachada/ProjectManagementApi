using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Removes a user from a project. The domain guards agains removing the
/// last Admin - that check produces a <see cref="DomainException"/> with
/// the global middleware maps <c>422 Unprocessable Entity</c>.
/// </summary>
/// <param name="ProjectId">The project to remove the member from.</param>
/// <param name="UserId">The user to remove.</param>
public sealed record RemoveProjectMember(Guid ProjectId, Guid UserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RemoveProjectMember"/>.
/// </summary>
/// <param name="db"></param>
public sealed class RemoveProjectMemberHandler(IAppDbContext db) : ICommandHandler<RemoveProjectMember>
{
    public async Task HandleAsync(RemoveProjectMember command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.RemoveMember(command.UserId);
        await db.SaveChangesAsync(cancellationToken);
    }
}
