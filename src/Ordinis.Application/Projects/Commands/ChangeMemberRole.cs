using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Changes an existing project member's role.
/// The domain guards agains demoting the last Admin.
/// </summary>
/// <param name="ProjectId">The project containing the member.</param>
/// <param name="UserId">The user whose role should change.</param>
/// <param name="NewRole">The new role to assign.</param>
public sealed record ChangeMemberRole(Guid ProjectId, Guid UserId, Role NewRole) : ICommand;

/// Handler
/// <summary>
/// Handles <see cref="ChangeMemberRole"/>.
/// </summary>
public sealed class ChangeMemberRoleHandler(IAppDbContext db) : ICommandHandler<ChangeMemberRole>
{
    public async Task HandleAsync(ChangeMemberRole command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.ChangeMemberRole(command.UserId, command.NewRole);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Validates <see cref="ChangeMemberRole"/> before the handler runs.
/// </summary>
public sealed class ChangeMemberRoleValidator : AbstractValidator<ChangeMemberRole>
{
    public ChangeMemberRoleValidator(IAppDbContext db)
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewRole).IsInEnum().WithMessage("Invalid role value.");
    }
}
