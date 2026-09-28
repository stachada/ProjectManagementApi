namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// Configuration options for <see cref="WebhookDeliveryDispatcherService"/>.
/// Bound from the <c>WebhookDelivery</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class WebhookDeliveryOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "WebhookDelivery";

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
}
