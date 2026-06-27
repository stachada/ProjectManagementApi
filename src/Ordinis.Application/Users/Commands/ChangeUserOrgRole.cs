using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

// Command
/// <summary>
/// Changes a user's organization-level role.
/// </summary>
/// <param name="UserId">The ID of the user whose role is being changed.</param>
/// <param name="NewOrgRole">The role to assign.</param>
/// <param name="RequestedByUserId">
/// The ID of the user performing the action. Only Admins may change
/// org-level roles - enforced by policy handlers.
/// </param>
/// <remarks>
/// Organization-level role (see cref="Role.Admin"/>, <see cref="Role.Member"/>,
/// <see cref="Role.Viewer"/>) governs organization-wide actions such as creating
/// projects and inviting users. Project-level permissions are separate and live
/// on <c>ProjectMember</c>.
/// </remarks>
public sealed record ChangeUserOrgRole(
    Guid UserId,
    Role NewOrgRole,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="ChangeUserOrgRole"/> commands.
/// </summary>
internal sealed class ChangeUserOrgRoleHandler(IAppDbContext db)
    : ICommandHandler<ChangeUserOrgRole>
{
    public async Task HandleAsync(ChangeUserOrgRole command, CancellationToken ct)
    {
        User user = await db.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, ct)
                ?? throw new NotFoundException(nameof(User), command.UserId);

        user.ChangeOrgRole(command.NewOrgRole);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                nameof(User),
                command.UserId,
                ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="ChangeUserOrgRole"/> commands before the handler runs.
/// </summary>
internal sealed class ChangeUserOrgRoleValidator : AbstractValidator<ChangeUserOrgRole>
{
    public ChangeUserOrgRoleValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.NewOrgRole)
            .IsInEnum();

        RuleFor(x => x.RequestedByUserId)
            .NotEmpty();
    }
}
