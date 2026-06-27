using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Commands;

// Command
/// <summary>
/// Creates a new active organization.
/// The slug is auto-generated from <see cref="Name"/> by the handler -
/// callers do not supply it directly.
/// </summary>
/// <param name="Name">Display name. Must be globally unique for slugification.</param>
/// <param name="Description">Optional description.</param>
public sealed record CreateOrganization(
    string Name,
    string? Description = null) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="CreateOrganization"/>. Slugifies the name via
/// <see cref="ISlugGenerator"/> and persists the new organization.
/// </summary>
public sealed class CreateOrganizationHandler(
    IAppDbContext db,
    ISlugGenerator slugGenerator) : ICommandHandler<CreateOrganization, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateOrganization command,
        CancellationToken cancellationToken = default)
    {
        var slug = slugGenerator.Slugify(command.Name);

        var organization = Organization.Create(
            name: command.Name,
            slug: slug,
            description: command.Description);

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);

        return organization.Id;
    }
}

// Validator
/// <summary>
/// Validates <see cref="CreateOrganization"/> before the handler runs.
/// Enforces name format and globally unique slug.
/// </summary>
public sealed class CreateOrganizationValidator : AbstractValidator<CreateOrganization>
{
    public CreateOrganizationValidator(
        IAppDbContext db,
        ISlugGenerator slugGenerator)
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .MustAsync(async (name, ct) =>
            {
                var slug = slugGenerator.Slugify(name);

                return !await db.Organizations.AnyAsync(o => o.Slug == slug, ct);
            })
            .WithMessage("An organization with this name already exists.");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description is not null);
    }
}
