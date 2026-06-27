using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Adds a user to a project with the specified role.
/// </summary>
/// <param name="ProjectId">The project to add the member to.</param>
/// <param name="UserId">The user to add. Must exist and not already be a member.</param>
/// <param name="Role">The role to assign within the project.</param>
public sealed record AddProjectMember(
    Guid ProjectId,
    Guid UserId,
    Role Role) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="AddProjectMember"/>.
/// Loads the project aggregate (with members) add delegates to
/// <see cref="Project.AddMember"/>. The duplicate-member invariant is also
/// enforced by the domain, but the validator catches it first for a clean 422.
/// </summary>
/// <param name="db"></param>
/// <param name="timeProvider"></param>
public sealed class AddProjectMemberHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<AddProjectMember>
{
    public async Task HandleAsync(AddProjectMember command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        DateTimeOffset now = timeProvider.GetUtcNow();
        project.AddMember(command.UserId, command.Role, now);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// Validator
/// <summary>
/// Validates <see cref="AddProjectMember"/> before the handler runs.
/// Checks project existence, user existence, role validity, and that
/// the user is not already a member.
/// </summary>
public sealed class AddProjectMemberValidator : AbstractValidator<AddProjectMember>
{
    public AddProjectMemberValidator(IAppDbContext db)
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await db.Projects.AnyAsync(p => p.Id == id, ct))
            .WithMessage("Project not found.");

        RuleFor(x => x.UserId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await db.Users.AnyAsync(u => u.Id == id, ct))
            .WithMessage("User not found.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
                !await db.ProjectMembers
                    .AnyAsync(m => m.ProjectId == command.ProjectId && m.UserId == command.UserId, ct))
            .WithMessage("User is already a member of the project.")
            .OverridePropertyName(nameof(AddProjectMember.UserId));
    }
}
