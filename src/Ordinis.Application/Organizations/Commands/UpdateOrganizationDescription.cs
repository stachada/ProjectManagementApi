using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Updates an organization's description.
/// Pass <c>null</c> for <see cref="NewDescription"/> to clear the it.
/// </summary>
/// <param name="OrganizationId">The organization to update.</param>
/// <param name="NewDescription">The new description, or <c>null</c> to clear.</param>
public sealed record UpdateOrganizationDescription(
    Guid OrganizationId,
    string? NewDescription) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UpdateOrganizationDescription"/>.
/// Loads the organization, calls <c>UpdateDescription</c>, and saves.
/// Catches <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// and translates it to <see cref="ConcurrencyException"/> for the API layer.
/// </summary>
public sealed class UpdateOrganizationDescriptionHandler(IAppDbContext db) : ICommandHandler<UpdateOrganizationDescription>
{
    public async Task HandleAsync(
        UpdateOrganizationDescription command,
        CancellationToken cancellationToken = default)
    {
        Organization organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Id == command.OrganizationId, cancellationToken)
                ?? throw new NotFoundException(nameof(Organization), command.OrganizationId);

        organization.UpdateDescription(command.NewDescription);

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
/// Validates <see cref="UpdateOrganizationDescription"/> before the handler runs.
/// </summary>
public sealed class UpdateOrganizationDescriptionValidator : AbstractValidator<UpdateOrganizationDescription>
{
    public UpdateOrganizationDescriptionValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty();

        RuleFor(x => x.NewDescription)
            .MaximumLength(1000)
            .When(x => x.NewDescription is not null);
    }
}
