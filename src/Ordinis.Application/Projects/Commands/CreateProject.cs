using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Creates a new project under the specified organization.
/// The slug is auto-generated from <see cref="Name"/> by the handler -
/// callers do not supply it directly.
/// </summary>
/// <param name="OrganizationId">The owning organization. Must exist and be active.</param>
/// <param name="CreatedByUserId">The user creating the project.</param>
/// <param name="Name">Display name. Must be unique within the organization after slugification.</param>
/// <param name="Description">Optional description.</param>
public sealed record CreateProject(
    Guid OrganizationId,
    Guid CreatedByUserId,
    string Name,
    string? Description = null) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="CreateProject"/>. Slugifies the name, checks slug
/// uniqueness within the organization, and persists the new project.
/// </summary>
public sealed class CreateProjectHandler(
    IAppDbContext db,
    TimeProvider timeProvider,
    ISlugGenerator slugGenerator) : ICommandHandler<CreateProject, Guid>
{
    public async Task<Guid> HandleAsync(CreateProject command, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        var slug = slugGenerator.Slugify(command.Name);

        var project = Project.Create(
            organizationId: command.OrganizationId,
            createdByUserId: command.CreatedByUserId,
            name: command.Name,
            slug: slug,
            now: now,
            description: command.Description);

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return project.Id;
    }
}

/// <summary>
/// Validates <see cref="CreateProject"/> before the handler runs.
/// Checks name format, organization existence, and slug uniqueness
/// scoped to the organization.
/// </summary>
public sealed class CreateProjectValidator : AbstractValidator<CreateProject>
{
    public CreateProjectValidator(
        IAppDbContext db,
        ISlugGenerator slugGenerator)
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await db.Organizations.AnyAsync(o => o.Id == id, ct))
            .WithMessage("Organization does not exist or is inactive.");

        RuleFor(x => x.CreatedByUserId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(100)
            .MustAsync(async (command, name, ct) =>
            {
                var slug = slugGenerator.Slugify(name);

                return !await db.Projects.AnyAsync(p => p.OrganizationId == command.OrganizationId && p.Slug == slug, ct);
            })
            .WithMessage("A project with the same name already exists in this organization.");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .When(x => x.Description is not null);
    }
}


