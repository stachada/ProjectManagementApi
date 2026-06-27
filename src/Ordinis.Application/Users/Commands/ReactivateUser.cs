using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Commands;

/// <summary>
/// Reactivates a previously deactivated user account.
/// </summary>
/// <param name="UserId">The ID of the user to reactivate.</param>
/// <param name="RequestedByUserId">
/// The ID of the user performing the action. Only Admins may reactivate
/// accounts — enforced by Phase 8 policy handlers.
/// </param>
public sealed record ReactivateUser(
    Guid UserId,
    Guid RequestedByUserId) : ICommand;

/// <summary>
/// Handles <see cref="ReactivateUser"/> commands.
/// </summary>
internal sealed class ReactivateUserHandler(IAppDbContext db)
    : ICommandHandler<ReactivateUser>
{
    public async Task HandleAsync(ReactivateUser command, CancellationToken ct)
    {
        User user = await db.Users
            .SingleOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException(nameof(User), command.UserId);

        // Domain guards: throws DomainException if already active.
        user.Reactivate();

        await db.SaveChangesAsync(ct);
    }
}
