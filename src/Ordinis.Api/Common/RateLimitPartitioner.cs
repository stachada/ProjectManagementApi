using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ordinis.Api.Common;

/// <summary>
/// Selects the rate limit partition (key + limiter algorithm) for a request. A pure function so
/// the authenticated-vs-anonymous branching can be unit tested against a fabricated
/// <see cref="ClaimsPrincipal"/> without a real JWT pipeline.
/// </summary>
public static class RateLimitPartitioner
{
    /// <summary>
    /// Authenticated requests get a sliding window keyed by user ID; anonymous requests get a
    /// fixed window keyed by client IP. <c>HttpContext.User</c> is never authenticated until
    /// Phase 8 (JWT auth) exists, so today every request takes the anonymous branch.
    /// </summary>
    public static RateLimitPartition<string> CreatePartition(HttpContext context, RateLimitingOptions options)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            string userKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.Identity.Name
                ?? "unknown";

            RateLimitingOptions.SlidingWindowSettings settings = options.SlidingWindow;
            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"user:{userKey}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    SegmentsPerWindow = settings.SegmentsPerWindow,
                    QueueLimit = 0,
                });
        }

        string ipKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        RateLimitingOptions.FixedWindowSettings fixedSettings = options.FixedWindow;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"ip:{ipKey}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = fixedSettings.PermitLimit,
                Window = TimeSpan.FromSeconds(fixedSettings.WindowSeconds),
                QueueLimit = 0,
            });
    }
}
