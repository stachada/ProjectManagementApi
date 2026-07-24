using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Soft-deletes a project, removing it from normal query results.
/// Distinct from archiving - soft-deleted projects cannot be restored
/// through the API. Prefer <see cref="ArchiveProject"/> for active
/// projects with existing task history.
/// </summary>
/// <param name="ProjectId">The project to soft-delete.</param>
/// <param name="IfMatch">
/// The project's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record DeleteProject(Guid ProjectId, byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="DeleteProject"/>.
/// </summary>
public sealed class DeleteProjectHandler(IAppDbContext db, TimeProvider timeProvider) : ICommandHandler<DeleteProject>
{
    public async Task HandleAsync(DeleteProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        ConcurrencyGuard.EnsureMatch(project.RowVersion, command.IfMatch, nameof(Project), command.ProjectId);

        DateTimeOffset now = timeProvider.GetUtcNow();
        project.SoftDelete(now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(nameof(Project), command.ProjectId, ex);
        }
    }
}

// Validator
/// <summary>
/// Validates <see cref="DeleteProject"/> commands.
/// </summary>
public sealed class DeleteProjectValidator : AbstractValidator<DeleteProject>
{
    public DeleteProjectValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
