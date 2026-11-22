using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Reactivates a suspended organization.
/// </summary>
/// <param name="OrganizationId">The organization to reactivate.</param>
/// <param name="IfMatch">
/// The expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// Compared against the current one before the change is applied (stale → 409).
/// </param>
public sealed record ReactivateOrganization(Guid OrganizationId, byte[]? IfMatch) : ICommand;

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

        ConcurrencyGuard.EnsureMatch(organization.RowVersion, command.IfMatch, nameof(Organization), command.OrganizationId);

        organization.Reactivate();

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
/// <summary>Validates <see cref="ReactivateOrganization"/> commands: the <c>If-Match</c> header is required.</summary>
public sealed class ReactivateOrganizationValidator : AbstractValidator<ReactivateOrganization>
{
    public ReactivateOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty();

        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
