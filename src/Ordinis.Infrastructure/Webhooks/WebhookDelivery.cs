namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// Tracks a single outbound HTTP delivery attempt (and retries) of one webhook event to one
/// registered <see cref="Ordinis.Domain.Projects.Webhook"/> URL.
/// </summary>
/// <remarks>
/// Infra-only bookkeeping row — same status as <c>OutboxMessage</c>, not a domain concept.
/// Written by <c>WebhookDomainEventHandler</c> (cheaply, inside <c>OutboxDispatcherJob</c>'s
/// transaction) and processed by the separate <see cref="WebhookDeliveryDispatcherService"/>,
/// which performs the actual HTTP call outside any DB transaction.
/// </remarks>
public sealed class WebhookDelivery
{
    /// <summary>UUIDv7 — time-ordered, client-side generated.</summary>
    public Guid Id { get; private set; }

    /// <summary>The webhook registration this delivery targets.</summary>
    public Guid WebhookId { get; private set; }

    /// <summary>
    /// A snapshot of the webhook's URL at enqueue time — a later URL change on the webhook
    /// registration must not retarget an in-flight delivery.
    /// </summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// The full JSON envelope (<c>{ event, occurredAt, data }</c>), built once at enqueue time
    /// so every retry sends byte-identical content.
    /// </summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Current delivery status.</summary>
    public WebhookDeliveryStatus Status { get; set; }

    /// <summary>Number of delivery attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Earliest time <see cref="WebhookDeliveryDispatcherService"/> should attempt (or retry)
    /// this delivery.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Last delivery error message, truncated to 2000 characters. Null until a failure occurs.</summary>
    public string? LastError { get; set; }

    /// <summary>Set once the delivery succeeds (a 2xx response was received).</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    private WebhookDelivery() { }

    /// <summary>
    /// Creates a new pending delivery, ready for immediate attempt.
    /// </summary>
    public static WebhookDelivery Create(Guid webhookId, string url, string payload, DateTimeOffset now)
        => new()
        {
            Id = Guid.CreateVersion7(),
            WebhookId = webhookId,
            Url = url,
            Payload = payload,
            Status = WebhookDeliveryStatus.Pending,
            AttemptCount = 0,
            NextAttemptAt = now
        };
}

/// <summary>The lifecycle status of a <see cref="WebhookDelivery"/>.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Not yet delivered; still eligible for (re)attempt.</summary>
    Pending,

    /// <summary>Delivered successfully (2xx response received).</summary>
    Delivered,

    /// <summary>Exhausted all retry attempts without a successful delivery.</summary>
    Failed
}
