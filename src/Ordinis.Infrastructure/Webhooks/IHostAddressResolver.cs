using System.Net;

namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// Resolves a host name to the IP addresses it currently points at — the lookup
/// <see cref="WebhookUrlGuard"/> classifies. A seam so the guard's decisions about host names
/// (not just IP literals) can be tested without depending on real DNS records.
/// </summary>
public interface IHostAddressResolver
{
    /// <summary>The host's current addresses.</summary>
    /// <exception cref="System.Net.Sockets.SocketException">The host does not resolve.</exception>
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default);
}

/// <summary>The production resolver: the operating system's DNS lookup.</summary>
public sealed class DnsHostAddressResolver : IHostAddressResolver
{
    /// <inheritdoc />
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
