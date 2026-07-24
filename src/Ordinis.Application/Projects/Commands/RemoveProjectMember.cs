using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Removes a user from a project. The domain guards agains removing the
/// last Admin - that check produces a <see cref="DomainException"/> with
/// the global middleware maps <c>422 Unprocessable Entity</c>.
/// </summary>
/// <param name="ProjectId">The project to remove the member from.</param>
/// <param name="UserId">The user to remove.</param>
/// <param name="IfMatch">
/// The project's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record RemoveProjectMember(Guid ProjectId, Guid UserId, byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="RemoveProjectMember"/>.
/// </summary>
/// <param name="db"></param>
public sealed class RemoveProjectMemberHandler(IAppDbContext db) : ICommandHandler<RemoveProjectMember>
{
    public async Task HandleAsync(RemoveProjectMember command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        ConcurrencyGuard.EnsureMatch(project.RowVersion, command.IfMatch, nameof(Project), command.ProjectId);

        project.RemoveMember(command.UserId);

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
/// Validates <see cref="RemoveProjectMember"/> commands.
/// </summary>
public sealed class RemoveProjectMemberValidator : AbstractValidator<RemoveProjectMember>
{
    public RemoveProjectMemberValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
