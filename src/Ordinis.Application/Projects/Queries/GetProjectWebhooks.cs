using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;

namespace Ordinis.Application.Projects.Queries;

// Query
/// <summary>
/// Returns all webhooks registered on a project.
/// Ordered by <c>RegisteredAt</c> ascending.
/// </summary>
/// <param name="ProjectId">The project whose webhooks to retrieve.</param>
public sealed record GetProjectWebhooks(Guid ProjectId) : IQuery<IReadOnlyList<WebhookDto>>;

// Handler
/// <summary>
/// Handles <see cref="GetProjectWebhooks"/>.
/// Projects webhook data directly from the DB without loading the full
/// <see cref="Project"/> aggregate.
/// </summary>
/// <param name="db"></param>
public sealed class GetProjectWebhooksHandler(IAppDbContext db)
    : IQueryHandler<GetProjectWebhooks, IReadOnlyList<WebhookDto>>
{
    public async Task<IReadOnlyList<WebhookDto>> HandleAsync(
        GetProjectWebhooks query,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects
            .AnyAsync(p => p.Id == query.ProjectId, cancellationToken);

        if (!projectExists)
        {
            throw new NotFoundException(nameof(Project), query.ProjectId);
        }

        List<Webhook> webhooks = await db.Webhooks
            .Where(w => w.ProjectId == query.ProjectId)
            .OrderBy(w => w.RegisteredAt)
            .ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);

        return webhooks.Select(w => w.ToWebhookDto()).ToList();
    }
}
