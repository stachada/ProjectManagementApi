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
}
