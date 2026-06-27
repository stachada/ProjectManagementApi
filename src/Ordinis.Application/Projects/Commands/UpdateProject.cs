using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Updates a project's display name and/or description.
/// The slug is intentionally excluded - it is immutable after creation.
/// </summary>
/// <param name="ProjectId">The project to update.</param>
/// <param name="NewName">New display name. Must not be empty.</param>
/// <param name="NewDescription">New description, or <c>null</c> to clear it.</param>
public sealed record UpdateProject(
    Guid ProjectId,
    string NewName,
    string? NewDescription) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UpdateProject"/>.
/// Loads the project, applies <c>Rename</c> and <c>UpdateDescription</c>,
/// and saves. Translates <see cref="DbUpdateConcurrencyException"/> to
/// <see cref="ConcurrencyException"/> for the API layer.
/// </summary>
/// <param name="db"></param>
public sealed class UpdateProjectHandler(IAppDbContext db) : ICommandHandler<UpdateProject>
{
    public async Task HandleAsync(UpdateProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        project.Rename(command.NewName);
        project.UpdateDescription(command.NewDescription);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                nameof(Project),
                command.ProjectId,
                ex);
        }
    }
}

/// <summary>
/// Validates <see cref="UpdateProject"/> before the handler runs.
/// </summary>
public sealed class UpdateProjectValidator : AbstractValidator<UpdateProject>
{
    public UpdateProjectValidator(IAppDbContext db)
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty();

        RuleFor(x => x.NewName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.NewDescription)
            .MaximumLength(1000)
            .When(x => x.NewDescription is not null);
    }
}
