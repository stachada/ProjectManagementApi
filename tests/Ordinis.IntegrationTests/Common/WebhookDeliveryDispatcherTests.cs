using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Persistence;
using Ordinis.Infrastructure.Webhooks;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Drives <see cref="WebhookDeliveryDispatcherService"/> directly against the real database, with
/// stubbed receivers. The shared host's own background jobs are stopped for the duration, so the
/// dispatcher under test is the only one processing deliveries.
/// </summary>
public sealed class WebhookDeliveryDispatcherTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    private const string SlowUrl = "https://slow.receiver.test/hook";
    private const string FastUrl = "https://fast.receiver.test/hook";

    /// <summary>
    /// The slow delivery is due first, so a dispatcher that delivers a batch one at a time would
    /// only reach the fast receiver after the slow one's HTTP timeout (10 s).
    /// </summary>
    [Fact]
    public async Task SlowReceiver_DoesNotDelayTheRestOfTheBatch()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await SeedAsync(db =>
        {
            db.WebhookDeliveries.Add(WebhookDelivery.Create(Guid.CreateVersion7(), SlowUrl, "{}", now.AddSeconds(-2)));
            db.WebhookDeliveries.Add(WebhookDelivery.Create(Guid.CreateVersion7(), FastUrl, "{}", now.AddSeconds(-1)));
            return db.SaveChangesAsync();
        });

        var receivers = new StubReceivers();
        List<IHostedService> hostJobs = Factory.Services.GetServices<IHostedService>()
            .Where(s => s.GetType().Namespace?.StartsWith("Ordinis.", StringComparison.Ordinal) == true)
            .ToList();

        foreach (IHostedService job in hostJobs)
        {
            await job.StopAsync(CancellationToken.None);
        }

        WebhookDeliveryDispatcherService dispatcher = CreateDispatcher(receivers);
        try
        {
            await dispatcher.StartAsync(CancellationToken.None);

            Task reached = receivers.FastReached.Task;
            Task first = await Task.WhenAny(reached, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(first == reached, "The fast receiver was not called while the slow one was still pending.");
            Assert.False(receivers.SlowFinished, "The slow receiver should still be pending.");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            foreach (IHostedService job in hostJobs)
            {
                await job.StartAsync(CancellationToken.None);
            }
        }
    }

    private WebhookDeliveryDispatcherService CreateDispatcher(StubReceivers receivers)
    {
        var options = new WebhookDeliveryOptions { PollingInterval = TimeSpan.FromMilliseconds(100) };

        return new WebhookDeliveryDispatcherService(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new StubClientFactory(receivers, options.HttpTimeout),
            new AllowEverything(),
            TimeProvider.System,
            NullLogger<WebhookDeliveryDispatcherService>.Instance,
            Factory.Services.GetRequiredService<IOptions<OutboxOptions>>(),
            Options.Create(options));
    }

    /// <summary>The slow receiver never answers (until cancelled); the fast one answers at once.</summary>
    private sealed class StubReceivers : HttpMessageHandler
    {
        public TaskCompletionSource FastReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SlowFinished { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.ToString() == SlowUrl)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    SlowFinished = true;
                }
            }

            FastReached.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubClientFactory(HttpMessageHandler handler, TimeSpan timeout) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = timeout };
    }

    private sealed class AllowEverything : IWebhookUrlGuard
    {
        public Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
