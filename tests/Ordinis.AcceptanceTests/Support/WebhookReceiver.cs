using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// A real loopback HTTP server (Kestrel) standing in for third-party webhook receivers. The
/// API's background dispatcher really POSTs to it, so delivery is exercised end-to-end.
/// Each named receiver in a scenario gets its own path under <see cref="BaseAddress"/>.
/// </summary>
internal sealed class WebhookReceiver : IAsyncDisposable
{
    private readonly ConcurrentQueue<ReceivedWebhook> _received = new();
    private WebApplication? _app;

    public Uri BaseAddress { get; private set; } = null!;

    public async Task StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.MapPost("/hooks/{receiver}", async (string receiver, HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            string body = await reader.ReadToEndAsync();
            _received.Enqueue(new ReceivedWebhook(receiver, request.ContentType, body));
            return Results.Ok();
        });

        await _app.StartAsync();

        string address = _app.Services
            .GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single();
        BaseAddress = new Uri(address);
    }

    /// <summary>The URL a webhook must be registered with to reach the receiver called <paramref name="name"/>.</summary>
    public string UrlFor(string name) => new Uri(BaseAddress, $"/hooks/{Uri.EscapeDataString(name)}").ToString();

    /// <summary>Whether <paramref name="url"/> points at this receiver (used to exempt it from the SSRF guard).</summary>
    public bool Owns(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == BaseAddress.Scheme
        && uri.Host == BaseAddress.Host
        && uri.Port == BaseAddress.Port;

    public IReadOnlyList<ReceivedWebhook> ReceivedBy(string name) =>
        _received.Where(r => r.Receiver == name).ToList();

    /// <summary>Forgets everything received — called between scenarios.</summary>
    public void Clear() => _received.Clear();

    /// <summary>
    /// Polls until a delivery to <paramref name="name"/> satisfies <paramref name="match"/>.
    /// Delivery is asynchronous: outbox poll (5 s) + delivery poll (1 s in tests).
    /// </summary>
    public async Task<ReceivedWebhook?> WaitForAsync(string name, Func<ReceivedWebhook, bool> match, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ReceivedWebhook? hit = ReceivedBy(name).FirstOrDefault(match);
            if (hit is not null)
            {
                return hit;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

/// <summary>One HTTP request received by <see cref="WebhookReceiver"/>.</summary>
internal sealed record ReceivedWebhook(string Receiver, string? ContentType, string Body)
{
    /// <summary>The envelope's <c>event</c> field, e.g. <c>task.moved</c>.</summary>
    public string? EventName => Parse().TryGetProperty("event", out JsonElement e) ? e.GetString() : null;

    /// <summary>The <c>TaskId</c> inside the envelope's <c>data</c> element, if present.</summary>
    public Guid? TaskId =>
        Parse().TryGetProperty("data", out JsonElement data) && data.TryGetProperty("TaskId", out JsonElement id)
            ? id.GetGuid()
            : null;

    private JsonElement Parse() => JsonDocument.Parse(Body).RootElement;
}
