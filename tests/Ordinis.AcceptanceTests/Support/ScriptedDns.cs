using System.Collections.Concurrent;
using System.Net;
using Ordinis.Infrastructure.Webhooks;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// The host resolver the test host's <see cref="WebhookUrlGuard"/> uses, so the feature files'
/// made-up hosts behave as the scenarios describe: every <c>*.example.com</c> host is public
/// (it resolves to <see cref="PublicAddress"/>), a scenario can point a host somewhere else
/// ("Given the host "intranet.example.com" resolves to 10.1.2.3"), and anything else — such as
/// <c>localhost</c> or <c>*.invalid</c> — goes to real DNS.
/// </summary>
/// <remarks>Only the guard consults it; it never makes the delivery client connect anywhere.</remarks>
internal sealed class ScriptedDns : IHostAddressResolver
{
    /// <summary>A public (TEST-NET-3 documentation) address — routable as far as the guard is concerned.</summary>
    public static readonly IPAddress PublicAddress = IPAddress.Parse("203.0.113.10");

    private readonly ConcurrentDictionary<string, IPAddress[]> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly DnsHostAddressResolver _realDns = new();

    /// <summary>Makes <paramref name="host"/> resolve to <paramref name="addresses"/> from now on.</summary>
    public void Point(string host, params IPAddress[] addresses) => _overrides[host] = addresses;

    /// <summary>Forgets every scenario's overrides.</summary>
    public void Reset() => _overrides.Clear();

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        if (_overrides.TryGetValue(host, out IPAddress[]? addresses))
        {
            return Task.FromResult(addresses);
        }

        return host.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase)
            ? Task.FromResult<IPAddress[]>([PublicAddress])
            : _realDns.ResolveAsync(host, cancellationToken);
    }
}
