using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Persistence;
using Ordinis.Infrastructure.Webhooks;
using Ordinis.Testing;
using Serilog.Core;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// The shared <see cref="OrdinisApiFactory"/>, adapted for acceptance scenarios: a random SQL
/// Server host port (so it can run alongside <c>Ordinis.IntegrationTests</c>), faster webhook
/// delivery polling, and a URL guard that additionally trusts the in-process
/// <see cref="WebhookReceiver"/>.
/// </summary>
internal sealed class AcceptanceApiFactory : OrdinisApiFactory
{
    private readonly WebhookReceiver _receiver;

    public AcceptanceApiFactory(WebhookReceiver receiver)
    {
        _receiver = receiver;

        // Read by WebApplication.CreateBuilder when the host is first built (see the base class
        // for why configuration must be supplied through environment variables).
        Environment.SetEnvironmentVariable("WebhookDelivery__PollingInterval", "00:00:01");

        // Retries are observable within a scenario; the production backoff (30 s, then 60 s) is
        // specified by the @needs-clock scenarios, which need a controllable clock instead.
        Environment.SetEnvironmentVariable("WebhookDelivery__InitialBackoff", "00:00:02");
    }

    protected override int? LocalHostPort => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWebhookUrlGuard>();
            services.AddSingleton<IWebhookUrlGuard>(new ReceiverAwareUrlGuard(_receiver, new WebhookUrlGuard(TestHost.Dns)));

            // "Given the database is unavailable" (off unless a scenario switches it on), and the
            // log capture Program.cs's Serilog setup picks up through ReadFrom.Services.
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(TestHost.Outage));
            services.AddSingleton<ILogEventSink>(TestHost.Logs);
        });
    }

    /// <summary>
    /// Allows the loopback <see cref="WebhookReceiver"/> (which the real guard rightly blocks)
    /// and delegates every other URL to the production <see cref="WebhookUrlGuard"/>, so SSRF
    /// rules still apply to everything else.
    /// </summary>
    private sealed class ReceiverAwareUrlGuard(WebhookReceiver receiver, IWebhookUrlGuard inner) : IWebhookUrlGuard
    {
        public Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default) =>
            receiver.Owns(url) ? Task.FromResult(true) : inner.IsAllowedAsync(url, cancellationToken);
    }
}
