namespace Ordinis.Infrastructure.Persistence;

/// <summary>
/// Configuration options for <see cref="OutboxDispatcherJob"/>.
/// <see cref="PollingInterval"/> is bound from the <c>Outbox</c> section of <c>appsettings.json</c>;
/// <see cref="DatabaseProvider"/> is set by <c>InfrastructureServiceExtensions.AddInfrastructureServices</c>
/// from the resolved <c>DatabaseProvider</c> setting.
/// </summary>
internal sealed class OutboxOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Outbox";

    /// <summary>How often the job polls for unprocessed messages.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The active database provider. Valid values: <c>"SqlServer"</c>, <c>"PostgreSQL"</c>.
    /// </summary>
    /// <remarks>
    /// Intentionally defaults to <see cref="string.Empty"/> rather than a provider name.
    /// A default of "SqlServer" would silently apply SQL Server locking hints in any host
    /// that registers <see cref="OutboxDispatcherJob"/> without calling
    /// <c>AddInfrastructureServices</c> (e.g. a PostgreSQL integration test host), causing
    /// the batch SELECT to fail with a T-SQL syntax error that is swallowed by
    /// <c>ExecuteAsync</c>'s catch block. An empty default guarantees the switch arm in
    /// <c>FetchBatchAsync</c> throws immediately, surfacing the misconfiguration early.
    /// </remarks>
    public string DatabaseProvider { get; set; } = string.Empty;
}
