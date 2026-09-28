namespace Ordinis.Application.Projects.Dtos;

/// <summary>
/// Represents a single registered webhook, returned by
/// <c>GET /api/v1/projects/{id}/webhooks</c>.
/// </summary>
public sealed record WebhookDto
{
    /// <summary>
    /// Webhook registration identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The URL that receives event deliveries.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Event names this webhook is subscribed to. Empty means all supported event types.
    /// </summary>
    public required IReadOnlyList<string> EventTypes { get; init; }

    /// <summary>
    /// The user who registered this webhook.
    /// </summary>
    public required Guid RegisteredByUserId { get; init; }

    /// <summary>
    /// UTC timestamp when the webhook was registered.
    /// </summary>
    public required DateTimeOffset RegisteredAt { get; init; }
}
