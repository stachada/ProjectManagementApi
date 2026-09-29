namespace Ordinis.Application.Common;

/// <summary>
/// Checks whether a webhook URL is safe to send an outbound request to — i.e. it does not
/// resolve to a loopback, private, link-local, or otherwise internal-network address.
/// </summary>
/// <remarks>
/// Guards against SSRF (Server-Side Request Forgery): without this check, a registered
/// webhook URL could point at <c>localhost</c>, an RFC 1918 private address, or a cloud
/// metadata endpoint (<c>169.254.169.254</c>), letting a webhook registration be used to
/// probe or attack internal infrastructure via this API's own outbound requests. Checked at
/// registration time (<c>RegisterWebhookValidator</c>) for an early, clear error, and again
/// at delivery time (<c>WebhookDeliveryDispatcherService</c>) immediately before every
/// attempt — re-checking on each attempt is what defeats DNS rebinding, where a hostname
/// resolves to a public address at registration but a private one by the time delivery
/// actually happens.
/// </remarks>
public interface IWebhookUrlGuard
{
    /// <summary>
    /// Resolves <paramref name="url"/>'s host and returns <c>true</c> only if every resolved
    /// address is a public, routable address. Returns <c>false</c> for a malformed URL, a
    /// non-<c>http</c>/<c>https</c> scheme, a host that fails to resolve, or a host that
    /// resolves to any loopback/private/link-local/reserved/multicast address.
    /// </summary>
    Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default);
}
