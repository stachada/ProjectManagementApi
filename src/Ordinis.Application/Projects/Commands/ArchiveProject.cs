using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Archives a project, making it read-only. Boards, tasks and history
/// remain accessible for audit purposes. Reversible via <c>UnarchiveProject</c>.
/// </summary>
/// <param name="ProjectId">The project to archive.</param>
/// <param name="IfMatch">
/// The project's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record ArchiveProject(Guid ProjectId, byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="ArchiveProject"/>.
/// </summary>
/// <param name="db"></param>
public sealed class ArchiveProjectHandler(IAppDbContext db) : ICommandHandler<ArchiveProject>
{
    public async Task HandleAsync(ArchiveProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        ConcurrencyGuard.EnsureMatch(project.RowVersion, command.IfMatch, nameof(Project), command.ProjectId);

        project.Archive();

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
/// Validates <see cref="ArchiveProject"/> commands.
/// </summary>
public sealed class ArchiveProjectValidator : AbstractValidator<ArchiveProject>
{
    public ArchiveProjectValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
