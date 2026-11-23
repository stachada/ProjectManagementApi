using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ordinis.Testing;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// Owns the expensive, run-wide resources: one SQL Server container + in-process API host and
/// one webhook receiver, started once before the first scenario and disposed after the last.
/// Each scenario starts from an empty database (reset in <see cref="AfterScenario"/>).
/// </summary>
/// <remarks>
/// A few scenarios need the API configured differently — in the Development or Production
/// environment, or with the production rate limiter on. Those run against variants of the main
/// host (<see cref="Development"/>, <see cref="Production"/>, <see cref="RateLimited"/>), built
/// on first use from the same configuration and database. Variants run no background jobs: only
/// the main host dispatches outbox messages and delivers webhooks, so nothing is processed twice.
/// </remarks>
[Binding]
internal sealed class TestHost
{
    /// <summary>
    /// Request header that sets the caller's IP address on the <see cref="RateLimited"/> host
    /// (TestServer has none), so scenarios can act "from" different addresses.
    /// </summary>
    public const string ClientAddressHeader = "X-Test-Client-Address";

    private static AcceptanceApiFactory? _factory;
    private static WebhookReceiver? _receiver;
    private static readonly Lazy<WebApplicationFactory<Program>> DevelopmentHost = new(() => Variant(environment: "Development"));
    private static readonly Lazy<WebApplicationFactory<Program>> ProductionHost = new(() => Variant(environment: "Production"));
    private static readonly Lazy<WebApplicationFactory<Program>> RateLimitedHost = new(() => Variant(rateLimited: true));

    public static AcceptanceApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("The test host has not been started.");

    public static WebhookReceiver Receiver =>
        _receiver ?? throw new InvalidOperationException("The webhook receiver has not been started.");

    /// <summary>Simulated database outage, shared by every host (they share one database).</summary>
    public static DatabaseOutage Outage { get; } = new();

    /// <summary>Every event any host logs.</summary>
    public static CapturedLogs Logs { get; } = new();

    public static WebApplicationFactory<Program> Development => DevelopmentHost.Value;

    public static WebApplicationFactory<Program> Production => ProductionHost.Value;

    public static WebApplicationFactory<Program> RateLimited => RateLimitedHost.Value;

    [BeforeTestRun]
    public static async Task StartAsync()
    {
        _receiver = new WebhookReceiver();
        await _receiver.StartAsync();

        _factory = new AcceptanceApiFactory(_receiver);
        await _factory.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task StopAsync()
    {
        // Disposing the main factory also disposes the variants derived from it.
        if (_factory is not null)
        {
            await ((IAsyncLifetime)_factory).DisposeAsync();
        }

        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }
    }

    [AfterScenario(Order = int.MaxValue)]
    public static async Task AfterScenario()
    {
        Outage.IsActive = false;
        await Factory.ResetDatabaseAsync();
        Receiver.Clear();
        Logs.Clear();
    }

    private static WebApplicationFactory<Program> Variant(string? environment = null, bool rateLimited = false) =>
        Factory.WithWebHostBuilder(builder =>
        {
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }

            builder.ConfigureTestServices(services =>
            {
                foreach (ServiceDescriptor job in services
                    .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Namespace?.StartsWith("Ordinis.", StringComparison.Ordinal) == true)
                    .ToList())
                {
                    services.Remove(job);
                }

                if (rateLimited)
                {
                    services.AddSingleton<KeepProductionRateLimiter>();
                    services.AddSingleton<IStartupFilter, ClientAddressFilter>();
                }
            });
        });

    /// <summary>Sets <c>RemoteIpAddress</c> from <see cref="ClientAddressHeader"/> before anything else runs.</summary>
    private sealed class ClientAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext context, RequestDelegate nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ClientAddressHeader, out Microsoft.Extensions.Primitives.StringValues address)
                    && IPAddress.TryParse(address.ToString(), out IPAddress? ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
