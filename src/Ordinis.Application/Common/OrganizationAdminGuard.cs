using Microsoft.EntityFrameworkCore;
using Ordinis.Domain.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Common;

/// <summary>
/// Keeps every organization administrable: it must always retain at least one <b>active</b>
/// org Admin. Mirrors the project-level <c>project.last-admin-demotion</c> /
/// <c>project.last-admin-removal</c> rules.
/// </summary>
/// <remarks>
/// <see cref="User"/> aggregates cannot see one another, so the "is this the last one?" question
/// is answered here, in the Application layer. Only active admins count: an inactive Admin cannot
/// sign in, so it cannot administer anything.
/// </remarks>
public static class OrganizationAdminGuard
{
    /// <summary>Throws (→ <c>422</c>) if demoting <paramref name="user"/> would leave the organization without an active Admin.</summary>
    public static Task EnsureNotLastAdminBeforeDemotionAsync(IAppDbContext db, User user, Role newRole, CancellationToken cancellationToken) =>
        newRole == Role.Admin
            ? Task.CompletedTask
            : EnsureAnotherActiveAdminAsync(db, user, "organization.last-admin-demotion",
                "The organization's last active Admin cannot be demoted. Promote another Admin first.", cancellationToken);

    /// <summary>Throws (→ <c>422</c>) if deactivating <paramref name="user"/> would leave the organization without an active Admin.</summary>
    public static Task EnsureNotLastAdminBeforeDeactivationAsync(IAppDbContext db, User user, CancellationToken cancellationToken) =>
        EnsureAnotherActiveAdminAsync(db, user, "organization.last-admin-deactivation",
            "The organization's last active Admin cannot be deactivated. Promote another Admin first.", cancellationToken);

    private static async Task EnsureAnotherActiveAdminAsync(
        IAppDbContext db, User user, string errorCode, string message, CancellationToken cancellationToken)
    {
        if (user.OrgRole != Role.Admin || !user.IsActive)
        {
            return; // not an active Admin: removing them changes nothing
        }

        bool anotherActiveAdmin = await db.Users.AnyAsync(
            u => u.OrganizationId == user.OrganizationId
                && u.Id != user.Id
                && u.OrgRole == Role.Admin
                && u.IsActive,
            cancellationToken);

        if (!anotherActiveAdmin)
        {
            throw new DomainException(message, errorCode);
        }
    }
}
