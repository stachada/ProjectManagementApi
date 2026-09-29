using System.Net;
using System.Net.Sockets;
using Ordinis.Application.Common;

namespace Ordinis.Infrastructure.Webhooks;

/// <summary>
/// <see cref="IWebhookUrlGuard"/> implementation backed by real DNS resolution
/// (<see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>).
/// </summary>
/// <remarks>
/// Stateless — registered as a singleton, same pattern as <c>ISlugGenerator</c>.
/// </remarks>
public sealed class WebhookUrlGuard : IWebhookUrlGuard
{
    /// <inheritdoc/>
    public async Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        IPAddress[] addresses;
        try
        {
            // A literal IP host (e.g. "http://127.0.0.1/hook") needs no DNS round trip —
            // Uri already canonicalizes alternate numeric encodings (decimal, octal, hex) to
            // a standard dotted-quad/IPv6 literal, so parsing uri.Host directly here is safe.
            addresses = uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                ? [IPAddress.Parse(uri.Host)]
                : await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // Host doesn't resolve — fail closed rather than treat "unknown" as "allowed".
            return false;
        }

        return addresses.Length > 0 && Array.TrueForAll(addresses, IsPublicAddress);
    }

    /// <summary>
    /// True if <paramref name="address"/> is a public, routable address — false for any
    /// loopback, private (RFC 1918), link-local (including the <c>169.254.169.254</c> cloud
    /// metadata endpoint), carrier-grade NAT, multicast, or reserved address.
    /// </summary>
    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = address.GetAddressBytes();
            var isDisallowed =
                b[0] == 10 ||                                  // 10.0.0.0/8 (private)
                (b[0] == 172 && b[1] is >= 16 and <= 31) ||     // 172.16.0.0/12 (private)
                (b[0] == 192 && b[1] == 168) ||                 // 192.168.0.0/16 (private)
                (b[0] == 169 && b[1] == 254) ||                 // 169.254.0.0/16 (link-local; covers the 169.254.169.254 cloud metadata endpoint)
                (b[0] == 100 && b[1] is >= 64 and <= 127) ||    // 100.64.0.0/10 (carrier-grade NAT)
                b[0] == 0 ||                                    // 0.0.0.0/8 ("this network")
                b[0] >= 224;                                    // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved, 255.255.255.255 broadcast
            return !isDisallowed;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast)
            {
                return false;
            }

            // fc00::/7 — unique local addresses (IPv6's equivalent of RFC 1918 private space).
            byte[] b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC)
            {
                return false;
            }
        }

        return true;
    }
}
