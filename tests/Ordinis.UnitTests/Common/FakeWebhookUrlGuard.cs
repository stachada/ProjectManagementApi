using Ordinis.Application.Common;

namespace Ordinis.UnitTests.Common;

/// <summary>
/// Returns a fixed allow/deny result for every call, so validator tests can exercise both
/// branches of <c>RegisterWebhookValidator</c>'s URL-safety rule without a real DNS lookup.
/// </summary>
internal sealed class FakeWebhookUrlGuard(bool isAllowed = true) : IWebhookUrlGuard
{
    public Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default)
        => Task.FromResult(isAllowed);
}
