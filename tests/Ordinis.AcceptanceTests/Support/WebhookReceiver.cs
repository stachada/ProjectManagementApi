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
/// A real HTTP server on loopback that stands in for webhook receivers. Each named receiver
/// ("Chat", "CI") is a path, <c>/hooks/{name}</c>; it records every request it gets — failed
/// attempts included — and answers as its script says (200 OK by default).
/// </summary>
/// <remarks>
/// The addresses the feature files give receivers ("https://chat.example.com/hooks/ordinis") are
/// labels: deliveries really go to <see cref="UrlFor"/>, which the test host's URL guard allows.
/// </remarks>
internal sealed class WebhookReceiver : IAsyncDisposable
{
    private readonly ConcurrentQueue<ReceivedWebhook> _received = new();
    private readonly ConcurrentDictionary<string, Func<int, ReceiverResponse>> _scripts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private WebApplication? _app;

    public Uri BaseAddress { get; private set; } = null!;

    public async Task StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.MapMethods("/hooks/{receiver}", ["POST", "PUT", "GET"], async (string receiver, HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            string body = await reader.ReadToEndAsync();
            int attempt = _attempts.AddOrUpdate(receiver, 1, (_, n) => n + 1);
            _received.Enqueue(new ReceivedWebhook(receiver, context.Request.Method, context.Request.ContentType, body, TestHost.Clock.GetUtcNow()));

            ReceiverResponse response = _scripts.TryGetValue(receiver, out Func<int, ReceiverResponse>? script)
                ? script(attempt)
                : ReceiverResponse.Ok;

            if (response.Abort)
            {
                context.Abort();
                return;
            }

            if (response.Hang is TimeSpan hang)
            {
                try
                {
                    await Task.Delay(hang, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return; // the client gave up waiting
                }
            }

            context.Response.StatusCode = response.Status;
            if (response.Location is not null)
            {
                context.Response.Headers.Location = response.Location;
            }
        });

        await _app.StartAsync();

        string address = _app.Services
            .GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single();
        BaseAddress = new Uri(address);
    }

    public string UrlFor(string name) => new Uri(BaseAddress, $"/hooks/{Uri.EscapeDataString(name)}").ToString();

    public bool Owns(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == BaseAddress.Scheme
        && uri.Host == BaseAddress.Host
        && uri.Port == BaseAddress.Port;

    /// <summary>How the receiver answers its Nth request (1-based) from now on.</summary>
    public void Script(string name, Func<int, ReceiverResponse> script) => _scripts[name] = script;

    public IReadOnlyList<ReceivedWebhook> ReceivedBy(string name) =>
        _received.Where(r => r.Receiver == name).ToList();

    public IReadOnlyList<ReceivedWebhook> ReceivedByAnyone => [.. _received];

    /// <summary>Forgets what was received (and the attempt numbering); scripts stay.</summary>
    public void ForgetReceived()
    {
        _received.Clear();
        _attempts.Clear();
    }

    /// <summary>Back to the initial state: nothing received, every receiver answering 200.</summary>
    public void Clear()
    {
        ForgetReceived();
        _scripts.Clear();
    }

    public async Task<ReceivedWebhook?> WaitForAsync(
        string name, Func<ReceivedWebhook, bool> match, TimeSpan timeout, Func<Task>? whileWaiting = null)
    {
        IReadOnlyList<ReceivedWebhook> hits = await WaitForCountAsync(name, match, 1, timeout, whileWaiting);
        return hits.FirstOrDefault();
    }

    /// <summary>
    /// Waits until <paramref name="count"/> matching requests arrived (or the timeout), returning
    /// the matches. <paramref name="whileWaiting"/> runs between checks (to skip time ahead, say).
    /// </summary>
    public async Task<IReadOnlyList<ReceivedWebhook>> WaitForCountAsync(
        string name, Func<ReceivedWebhook, bool> match, int count, TimeSpan timeout, Func<Task>? whileWaiting = null)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            List<ReceivedWebhook> hits = ReceivedBy(name).Where(match).ToList();
            if (hits.Count >= count || DateTime.UtcNow >= deadline)
            {
                return hits;
            }

            if (whileWaiting is not null)
            {
                await whileWaiting();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

/// <summary>How a scripted receiver answers one request.</summary>
/// <param name="Status">HTTP status to answer with.</param>
/// <param name="Location">A <c>Location</c> header (for redirects).</param>
/// <param name="Hang">Wait this long before answering (or until the client gives up).</param>
/// <param name="Abort">Drop the connection without answering.</param>
internal sealed record ReceiverResponse(int Status = 200, string? Location = null, TimeSpan? Hang = null, bool Abort = false)
{
    public static readonly ReceiverResponse Ok = new();
}

internal sealed record ReceivedWebhook(string Receiver, string Method, string? ContentType, string Body, DateTimeOffset ReceivedAt)
{
    public string? EventName => Parse().TryGetProperty("event", out JsonElement e) ? e.GetString() : null;

    public Guid? TaskId =>
        Parse().TryGetProperty("data", out JsonElement data) && data.TryGetProperty("TaskId", out JsonElement id)
            ? id.GetGuid()
            : null;

    public JsonElement Json => Parse();

    private JsonElement Parse() => JsonDocument.Parse(Body).RootElement.Clone();
}
