namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// Configuration options for <see cref="WebhookDeliveryDispatcherService"/>.
/// Bound from the <c>WebhookDelivery</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class WebhookDeliveryOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "WebhookDelivery";

    /// <summary>
    /// Name of the <see cref="System.Net.Http.IHttpClientFactory"/> client used for outbound
    /// deliveries. That client never follows redirects — see <c>InfrastructureServiceExtensions</c>.
    /// </summary>
    public const string HttpClientName = "WebhookDelivery";

    /// <summary>How often the dispatcher polls for due deliveries.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum number of due deliveries fetched per poll tick.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Total attempts (including the first) before a delivery is marked <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry. Subsequent retries multiply this by <see cref="BackoffMultiplier"/>.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Multiplier applied to the backoff delay after each failed attempt.</summary>
    public double BackoffMultiplier { get; set; } = 2;

    /// <summary>Per-request timeout for the outbound delivery HTTP call.</summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many deliveries of one batch are sent at the same time, so a slow receiver delays
    /// only its own delivery, not the rest of the batch.
    /// </summary>
    public int MaxParallelDeliveries { get; set; } = 8;
}
