using System.Net;
using Ordinis.Infrastructure.Webhooks;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Verifies <see cref="WebhookUrlGuard"/>'s SSRF address-classification rules. Uses IP-literal
/// URLs throughout (e.g. <c>http://127.0.0.1/</c>) rather than hostnames, so these run fast and
/// deterministically with no real DNS lookup — despite living in the integration test project,
/// this class needs no database or hosted app, only the real <c>Ordinis.Infrastructure</c>
/// implementation (unavailable to <c>Ordinis.UnitTests</c>, which references Application only).
/// </summary>
public sealed class WebhookUrlGuardTests
{
    private readonly WebhookUrlGuard _guard = new();

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://127.255.255.255/hook")]
    [InlineData("http://10.0.0.1/hook")]
    [InlineData("http://10.255.255.255/hook")]
    [InlineData("http://172.16.0.1/hook")]
    [InlineData("http://172.31.255.255/hook")]
    [InlineData("http://192.168.0.1/hook")]
    [InlineData("http://169.254.169.254/hook")] // cloud metadata endpoint
    [InlineData("http://100.64.0.1/hook")]      // carrier-grade NAT
    [InlineData("http://0.0.0.0/hook")]
    [InlineData("http://255.255.255.255/hook")]
    [InlineData("http://224.0.0.1/hook")]       // multicast
    [InlineData("http://[::1]/hook")]           // IPv6 loopback
    [InlineData("http://[fc00::1]/hook")]       // IPv6 unique local
    [InlineData("http://[fe80::1]/hook")]       // IPv6 link-local
    public async Task IsAllowedAsync_DisallowedAddress_ReturnsFalse(string url)
    {
        Assert.False(await _guard.IsAllowedAsync(url));
    }

    [Theory]
    [InlineData("http://1.1.1.1/hook")]
    [InlineData("http://8.8.8.8/hook")]
    [InlineData("http://172.15.255.255/hook")]  // just outside 172.16.0.0/12
    [InlineData("http://172.32.0.0/hook")]      // just outside 172.16.0.0/12
    [InlineData("http://[2606:4700:4700::1111]/hook")] // Cloudflare IPv6
    public async Task IsAllowedAsync_PublicAddress_ReturnsTrue(string url)
    {
        Assert.True(await _guard.IsAllowedAsync(url));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://127.0.0.1/hook")]
    [InlineData("")]
    public async Task IsAllowedAsync_MalformedOrWrongScheme_ReturnsFalse(string url)
    {
        Assert.False(await _guard.IsAllowedAsync(url));
    }

    [Fact]
    public async Task IsAllowedAsync_HostDoesNotResolve_ReturnsFalse()
    {
        Assert.False(await _guard.IsAllowedAsync("http://this-host-does-not-exist.invalid/hook"));
    }

    // ---- Host names, through a scripted resolver (no real DNS) ----

    [Theory]
    [InlineData("203.0.113.10")]
    [InlineData("2606:4700:4700::1111")]
    public async Task IsAllowedAsync_HostResolvingToPublicAddresses_ReturnsTrue(string address)
    {
        var guard = new WebhookUrlGuard(new ScriptedResolver(IPAddress.Parse(address)));

        Assert.True(await guard.IsAllowedAsync("https://hooks.example.com/hook"));
    }

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("fd00::1")]
    public async Task IsAllowedAsync_HostResolvingToAnInternalAddress_ReturnsFalse(string address)
    {
        var guard = new WebhookUrlGuard(new ScriptedResolver(IPAddress.Parse(address)));

        Assert.False(await guard.IsAllowedAsync("https://intranet.example.com/hook"));
    }

    /// <summary>One internal address among public ones is enough: the client may connect to any of them.</summary>
    [Fact]
    public async Task IsAllowedAsync_HostResolvingToPublicAndInternalAddresses_ReturnsFalse()
    {
        var guard = new WebhookUrlGuard(new ScriptedResolver(IPAddress.Parse("203.0.113.10"), IPAddress.Parse("10.0.0.5")));

        Assert.False(await guard.IsAllowedAsync("https://mixed.example.com/hook"));
    }

    [Fact]
    public async Task IsAllowedAsync_HostResolvingToNoAddress_ReturnsFalse()
    {
        var guard = new WebhookUrlGuard(new ScriptedResolver());

        Assert.False(await guard.IsAllowedAsync("https://empty.example.com/hook"));
    }

    [Fact]
    public async Task IsAllowedAsync_IpLiteral_DoesNotConsultTheResolver()
    {
        var resolver = new ScriptedResolver(IPAddress.Parse("10.0.0.5"));
        var guard = new WebhookUrlGuard(resolver);

        Assert.True(await guard.IsAllowedAsync("http://1.1.1.1/hook"));
        Assert.Equal(0, resolver.Lookups);
    }

    private sealed class ScriptedResolver(params IPAddress[] addresses) : IHostAddressResolver
    {
        public int Lookups { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(addresses);
        }
    }
}
