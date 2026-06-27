using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Suspends an active organization.
/// While suspended, no new projects or users can be added.
/// </summary>
/// <param name="OrganizationId">The organization to suspend.</param>
public sealed record SuspendOrganization(Guid OrganizationId) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="SuspendOrganization"/>.
/// Loads the organization, calls <c>Suspend</c>, and saves.
/// The domain enforces that an already-suspended organization cannot be suspended again.
/// </summary>
public sealed class SuspendOrganizationHandler(
    IAppDbContext db) : ICommandHandler<SuspendOrganization>
{
    public async Task HandleAsync(
        SuspendOrganization command,
        CancellationToken cancellationToken = default)
    {
        var organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        organization.Suspend();

        await db.SaveChangesAsync(cancellationToken);
    }
}
