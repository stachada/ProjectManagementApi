using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Infrastructure.Common;
using Ordinis.Infrastructure.Webhooks;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Verifies that the named <see cref="WebhookDeliveryOptions.HttpClientName"/> client, as
/// registered by <see cref="InfrastructureServiceExtensions.AddInfrastructureServices"/>, never
/// follows redirects. <c>WebhookUrlGuard</c> only vets the registered URL, so following a 3xx
/// would let a public receiver bounce deliveries to internal addresses (SSRF).
/// </summary>
/// <remarks>
/// Like <see cref="WebhookUrlGuardTests"/>, this needs no database or hosted Ordinis app: it
/// builds the real Infrastructure service registrations (the DbContext is registered but never
/// resolved) and points the client at a throwaway loopback Kestrel server whose <c>/hook</c>
/// endpoint redirects to <c>/target</c>.
/// </remarks>
public sealed class WebhookDeliveryHttpClientTests : IAsyncLifetime
{
    private WebApplication _receiver = null!;
    private Uri _receiverBaseAddress = null!;
    private ServiceProvider _infrastructure = null!;
    private int _targetHits;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _receiver = builder.Build();

        _receiver.MapPost("/hook/{status:int}", (int status) => status switch
        {
            301 => Results.Redirect("/target", permanent: true),
            302 => Results.Redirect("/target", permanent: false),
            307 => Results.Redirect("/target", permanent: false, preserveMethod: true),
            308 => Results.Redirect("/target", permanent: true, preserveMethod: true),
            _ => Results.StatusCode(StatusCodes.Status400BadRequest)
        });
        _receiver.Map("/target", () =>
        {
            Interlocked.Increment(ref _targetHits);
            return Results.Ok();
        });

        await _receiver.StartAsync();

        string address = _receiver.Services
            .GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single();
        _receiverBaseAddress = new Uri(address);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "SqlServer",
                ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused;TrustServerCertificate=True"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(configuration);
        _infrastructure = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _infrastructure.DisposeAsync();
        await _receiver.DisposeAsync();
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task DeliveryClient_ReceiverRedirects_ReturnsRedirectWithoutFollowingIt(int redirectStatus)
    {
        HttpClient client = _infrastructure
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(WebhookDeliveryOptions.HttpClientName);

        using var content = new StringContent("""{"event":"task.created"}""", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(new Uri(_receiverBaseAddress, $"/hook/{redirectStatus}"), content);

        Assert.Equal((HttpStatusCode)redirectStatus, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(0, Volatile.Read(ref _targetHits));
    }

    /// <summary>
    /// Control: proves the fixture would catch a regression — a plain <see cref="HttpClient"/>
    /// (default <c>AllowAutoRedirect = true</c>) does follow the same redirect to <c>/target</c>.
    /// </summary>
    [Fact]
    public async Task DefaultClient_ReceiverRedirects_FollowsRedirect()
    {
        using var client = new HttpClient();

        using var content = new StringContent("""{"event":"task.created"}""", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(new Uri(_receiverBaseAddress, "/hook/307"), content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, Volatile.Read(ref _targetHits));
    }
}
