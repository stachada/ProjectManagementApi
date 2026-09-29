using Ordinis.Api.Common;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Commands;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;

namespace Ordinis.Api.MinimalApis;

/// <summary>
/// Registers the project webhook Minimal API endpoints — per <c>CLAUDE.md</c>'s API style,
/// webhooks are a focused, non-resource-CRUD concern and belong in a Minimal API, not a
/// controller.
/// </summary>
public static class WebhookEndpoints
{
    /// <summary>
    /// Maps <c>POST /api/v1/projects/{id}/webhooks</c>, <c>DELETE .../webhooks/{webhookId}</c>,
    /// and <c>GET .../webhooks</c>.
    /// </summary>
    /// <param name="routes">The endpoint route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/projects/{id:guid}/webhooks",
            async (
                Guid id,
                RegisterWebhookRequest request,
                IDispatcher dispatcher,
                HttpContext context,
                CancellationToken ct) =>
            {
                byte[]? ifMatch = context.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[];

                var command = new RegisterWebhook(
                    id,
                    request.Url,
                    request.EventTypes ?? [],
                    request.RegisteredByUserId,
                    ifMatch);

                Guid webhookId = await dispatcher.SendAsync<RegisterWebhook, Guid>(command, ct);

                IReadOnlyList<WebhookDto> webhooks = await dispatcher.QueryAsync<GetProjectWebhooks, IReadOnlyList<WebhookDto>>(
                    new GetProjectWebhooks(id), ct);

                WebhookDto dto = webhooks.Single(w => w.Id == webhookId);

                return Results.Created($"/api/v1/projects/{id}/webhooks", dto);
            })
            .WithMetadata(new IdempotentAttribute())
            .Produces<WebhookDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        routes.MapDelete("/api/v1/projects/{id:guid}/webhooks/{webhookId:guid}",
            async (
                Guid id,
                Guid webhookId,
                IDispatcher dispatcher,
                HttpContext context,
                CancellationToken ct) =>
            {
                byte[]? ifMatch = context.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[];

                await dispatcher.SendAsync(new UnregisterWebhook(id, webhookId, ifMatch), ct);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        routes.MapGet("/api/v1/projects/{id:guid}/webhooks",
            async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                IReadOnlyList<WebhookDto> webhooks = await dispatcher.QueryAsync<GetProjectWebhooks, IReadOnlyList<WebhookDto>>(
                    new GetProjectWebhooks(id), ct);

                return Results.Ok(webhooks);
            })
            .Produces<IReadOnlyList<WebhookDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return routes;
    }
}

/// <summary>
/// Request body for <c>POST /api/v1/projects/{id}/webhooks</c>.
/// </summary>
/// <param name="Url">The absolute <c>http</c>/<c>https</c> URL to deliver events to.</param>
/// <param name="EventTypes">
/// Event names to subscribe to (see <see cref="WebhookEventTypes"/>). Omit or send an empty
/// list to subscribe to all supported event types.
/// </param>
/// <param name="RegisteredByUserId">The user registering the webhook.</param>
public sealed record RegisterWebhookRequest(string Url, IReadOnlyList<string>? EventTypes, Guid RegisteredByUserId);
