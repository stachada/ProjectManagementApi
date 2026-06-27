using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

// Command
/// <summary>
/// Deactivates a user account.
/// </summary>
/// <param name="UserId">The ID of the user to deactivate.</param>
/// <param name="RequestedByUserId">
/// The ID of the user performing the action. Only Admins may deactivate
/// other accounts — enforced by Phase 8 policy handlers.
/// </param>
/// <remarks>
/// Prefer deactivation over deletion to preserve audit trail integrity.
/// Deactivated users cannot authenticate and are excluded from assignee
/// suggestions, but their historical contributions (tasks, comments) remain
/// intact and attributed.
/// </remarks>
public sealed record DeactivateUser(
    Guid UserId,
    Guid RequestedByUserId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="DeactivateUser"/> commands.
/// </summary>
internal sealed class DeactivateUserHandler(IAppDbContext db)
    : ICommandHandler<DeactivateUser>
{
    public async Task HandleAsync(DeactivateUser command, CancellationToken ct)
    {
        User user = await db.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException(nameof(User), command.UserId);

        // Domain guards: throws DomainException if already inactive.
        user.Deactivate();

        await db.SaveChangesAsync(ct);
    }
}
