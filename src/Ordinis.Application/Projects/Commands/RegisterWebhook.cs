using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Commands;

// Command
/// <summary>
/// Registers a new webhook URL on a project. The registered URL receives an HTTP POST
/// delivery whenever one of <paramref name="EventTypes"/> fires for a task in the project.
/// </summary>
/// <param name="ProjectId">The project to register the webhook on.</param>
/// <param name="Url">The absolute <c>http</c>/<c>https</c> URL to deliver events to.</param>
/// <param name="EventTypes">
/// Event names to subscribe to (see <see cref="WebhookEventTypes"/>). Empty subscribes to
/// all supported event types.
/// </param>
/// <param name="RegisteredByUserId">The user registering the webhook.</param>
/// <param name="IfMatch">
/// The project's expected <c>RowVersion</c>, decoded from the request's <c>If-Match</c> header.
/// </param>
public sealed record RegisterWebhook(
    Guid ProjectId,
    string Url,
    IReadOnlyList<string> EventTypes,
    Guid RegisteredByUserId,
    byte[]? IfMatch) : ICommand<Guid>;

// Handler
/// <summary>
/// Handles <see cref="RegisterWebhook"/>. Loads the project aggregate (with webhooks) and
/// delegates to <see cref="Project.RegisterWebhook"/>.
/// </summary>
/// <param name="db"></param>
/// <param name="timeProvider"></param>
public sealed class RegisterWebhookHandler(
    IAppDbContext db,
    TimeProvider timeProvider) : ICommandHandler<RegisterWebhook, Guid>
{
    public async Task<Guid> HandleAsync(RegisterWebhook command, CancellationToken cancellationToken = default)
    {
        Project project = await db.Projects
            .Include(p => p.Webhooks)
            .SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
                ?? throw new NotFoundException(nameof(Project), command.ProjectId);

        ConcurrencyGuard.EnsureMatch(project.RowVersion, command.IfMatch, nameof(Project), command.ProjectId);

        DateTimeOffset now = timeProvider.GetUtcNow();
        Webhook webhook = project.RegisterWebhook(command.Url, command.EventTypes, command.RegisteredByUserId, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(nameof(Project), command.ProjectId, ex);
        }

        return webhook.Id;
    }
}

// Validator
/// <summary>
/// Validates <see cref="RegisterWebhook"/> before the handler runs.
/// Checks project existence, URL well-formedness, and event type validity.
/// </summary>
public sealed class RegisterWebhookValidator : AbstractValidator<RegisterWebhook>
{
    public RegisterWebhookValidator(IAppDbContext db, IWebhookUrlGuard urlGuard)
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await db.Projects.AnyAsync(p => p.Id == id, ct))
            .WithMessage("Project not found.");

        RuleFor(x => x.Url)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Url must be an absolute http or https URL.")
            .MustAsync(async (url, ct) => await urlGuard.IsAllowedAsync(url, ct))
            .WithMessage("Url resolves to a private, loopback, or otherwise disallowed network address.");

        RuleForEach(x => x.EventTypes)
            .Must(WebhookEventTypes.All.Contains)
            .WithMessage($"EventTypes may only contain: {string.Join(", ", WebhookEventTypes.All)}.");

        RuleFor(x => x.RegisteredByUserId)
            .NotEmpty()
            .MustAsync(async (id, ct) => await db.Users.AnyAsync(u => u.Id == id, ct))
            .WithMessage("User not found.");

        RuleFor(x => x.IfMatch)
            .NotNull()
            .WithMessage("If-Match header is required.");
    }
}
