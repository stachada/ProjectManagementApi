using FluentValidation;
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
/// <param name="IfMatch">
/// The expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// Compared against the current one before the change is applied (stale → 409).
/// </param>
public sealed record SuspendOrganization(Guid OrganizationId, byte[]? IfMatch) : ICommand;

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

        ConcurrencyGuard.EnsureMatch(organization.RowVersion, command.IfMatch, nameof(Organization), command.OrganizationId);

        organization.Suspend();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(nameof(Organization), command.OrganizationId, ex);
        }
    }
}

// Validator
/// <summary>Validates <see cref="SuspendOrganization"/> commands: the <c>If-Match</c> header is required.</summary>
public sealed class SuspendOrganizationValidator : AbstractValidator<SuspendOrganization>
{
    public SuspendOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty();

        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
