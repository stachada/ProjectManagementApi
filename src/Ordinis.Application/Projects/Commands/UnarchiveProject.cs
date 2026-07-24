using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Restores an archived project to active status.
/// </summary>
/// <param name="ProjectId">The project to unarchive.</param>
/// <param name="IfMatch">
/// The project's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record UnarchiveProject(Guid ProjectId, byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UnarchiveProject"/>.
/// </summary>
/// <param name="db"></param>
public sealed class UnarchiveProjectHandler(IAppDbContext db) : ICommandHandler<UnarchiveProject>
{
    public async Task HandleAsync(UnarchiveProject command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        ConcurrencyGuard.EnsureMatch(project.RowVersion, command.IfMatch, nameof(Project), command.ProjectId);

        project.Unarchive();

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
/// Validates <see cref="UnarchiveProject"/> commands.
/// </summary>
public sealed class UnarchiveProjectValidator : AbstractValidator<UnarchiveProject>
{
    public UnarchiveProjectValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
