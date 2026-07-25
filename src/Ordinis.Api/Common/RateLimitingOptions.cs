namespace Ordinis.Api.Common;

/// <summary>
/// Configuration options for the global rate limiter (see <see cref="RateLimitPartitioner"/>).
/// Bound from the <c>RateLimiting</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Applied per client IP to unauthenticated requests.</summary>
    public FixedWindowSettings FixedWindow { get; set; } = new();

    /// <summary>
    /// Applied per user ID to authenticated requests. Dormant until Phase 8 (JWT auth) makes
    /// <c>HttpContext.User</c> an authenticated principal — <see cref="RateLimitPartitioner"/>
    /// falls back to <see cref="FixedWindow"/> until then.
    /// </summary>
    public SlidingWindowSettings SlidingWindow { get; set; } = new();

    public sealed class FixedWindowSettings
    {
        /// <summary>Maximum requests allowed per window.</summary>
        public int PermitLimit { get; set; } = 100;

        /// <summary>Window length, in seconds.</summary>
        public int WindowSeconds { get; set; } = 60;
    }

    public sealed class SlidingWindowSettings
    {
        /// <summary>Maximum requests allowed per window.</summary>
        public int PermitLimit { get; set; } = 500;

        /// <summary>Window length, in seconds.</summary>
        public int WindowSeconds { get; set; } = 60;

        /// <summary>Number of segments the window is divided into for the sliding calculation.</summary>
        public int SegmentsPerWindow { get; set; } = 6;
    }
}
