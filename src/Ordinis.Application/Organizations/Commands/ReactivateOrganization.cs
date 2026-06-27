using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Reactivates a suspended organization.
/// </summary>
/// <param name="OrganizationId">The organization to reactivate.</param>
public sealed record ReactivateOrganization(Guid OrganizationId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="ReactivateOrganization"/>.
/// Loads the organization, calls <c>Reactivate</c>, and saves.
/// The domain enforces that an already-active organization cannot be reactivated again.
/// </summary>
public sealed class ReactivateOrganizationHandler(
    IAppDbContext db) : ICommandHandler<ReactivateOrganization>
{
    public async Task HandleAsync(
        ReactivateOrganization command,
        CancellationToken cancellationToken = default)
    {
        var organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        organization.Reactivate();

        await db.SaveChangesAsync(cancellationToken);
    }
}
