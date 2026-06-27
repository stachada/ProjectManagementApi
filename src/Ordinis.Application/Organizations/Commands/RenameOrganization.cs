using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Renames an organization's display name.
/// The slug is intentionally not updated - slugs are immutable after creation.
/// </summary>
/// <param name="OrganizationId">The organization to rename.</param>
/// <param name="NewName">The new display name.</param>
public sealed record RenameOrganization(
    Guid OrganizationId,
    string NewName) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RenameOrganization"/>.
/// Loads the organization, calls <c>Rename</c>, and saves.
/// Catches <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// and translates it to <see cref="ConcurrencyException"/> for the API layer.
/// </summary>
/// <param name="db"></param>
public sealed class RenameOrganizationHandler(IAppDbContext db) : ICommandHandler<RenameOrganization>
{
    public async Task HandleAsync(
        RenameOrganization command,
        CancellationToken cancellationToken = default)
    {
        Organization organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
                ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        organization.Rename(command.NewName);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                nameof(Organization),
                command.OrganizationId,
                ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="RenameOrganization"/> before the handler runs.
/// </summary>
public sealed class RenameOrganizationValidator : AbstractValidator<RenameOrganization>
{
    public RenameOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty();

        RuleFor(x => x.NewName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
