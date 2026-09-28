using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Removes a registered webhook from a project.
/// </summary>
/// <param name="ProjectId">The project the webhook is registered on.</param>
/// <param name="WebhookId">The webhook to remove.</param>
/// <param name="IfMatch">
/// The project's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record UnregisterWebhook(Guid ProjectId, Guid WebhookId, byte[]? IfMatch) : ICommand;

// Handler
/// <summary>
/// Handles <see cref="UnregisterWebhook"/>.
/// </summary>
/// <param name="db"></param>
public sealed class UnregisterWebhookHandler(IAppDbContext db) : ICommandHandler<UnregisterWebhook>
{
    public async Task HandleAsync(UnregisterWebhook command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Webhooks)
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        ConcurrencyGuard.EnsureMatch(project.RowVersion, command.IfMatch, nameof(Project), command.ProjectId);

        project.UnregisterWebhook(command.WebhookId);

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
/// Validates <see cref="UnregisterWebhook"/> commands.
/// </summary>
public sealed class UnregisterWebhookValidator : AbstractValidator<UnregisterWebhook>
{
    public UnregisterWebhookValidator()
    {
        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
