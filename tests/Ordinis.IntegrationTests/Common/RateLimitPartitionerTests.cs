using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Ordinis.Api.Common;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Exercises <see cref="RateLimitPartitioner"/>'s authenticated-vs-anonymous branching directly
/// against a fabricated <see cref="HttpContext"/> — no real JWT auth exists yet (Phase 8), so this
/// is the only way to cover the sliding-window-per-user path before then. See
/// <c>docs/API_INFRASTRUCTURE.md</c> and BUILD_PLAN.md's Phase 7 rate limiting notes.
/// </summary>
public sealed class RateLimitPartitionerTests
{
    private static readonly RateLimitingOptions Options = new()
    {
        FixedWindow = new RateLimitingOptions.FixedWindowSettings { PermitLimit = 100, WindowSeconds = 60 },
        SlidingWindow = new RateLimitingOptions.SlidingWindowSettings { PermitLimit = 500, WindowSeconds = 60, SegmentsPerWindow = 6 },
    };

    [Fact]
    public void CreatePartition_AnonymousRequest_UsesFixedWindowKeyedByIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");

        RateLimitPartition<string> partition = RateLimitPartitioner.CreatePartition(context, Options);
        RateLimiter limiter = partition.Factory(partition.PartitionKey);

        Assert.Equal("ip:203.0.113.42", partition.PartitionKey);
        Assert.Contains("FixedWindow", limiter.GetType().Name);
    }

    [Fact]
    public void CreatePartition_AnonymousRequest_NoRemoteIp_FallsBackToUnknownKey()
    {
        var context = new DefaultHttpContext();

        RateLimitPartition<string> partition = RateLimitPartitioner.CreatePartition(context, Options);

        Assert.Equal("ip:unknown", partition.PartitionKey);
    }

    [Fact]
    public void CreatePartition_AuthenticatedRequest_UsesSlidingWindowKeyedByUserId()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-123")],
            authenticationType: "Test");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        RateLimitPartition<string> partition = RateLimitPartitioner.CreatePartition(context, Options);
        RateLimiter limiter = partition.Factory(partition.PartitionKey);

        Assert.Equal("user:user-123", partition.PartitionKey);
        Assert.Contains("SlidingWindow", limiter.GetType().Name);
    }

    [Fact]
    public void CreatePartition_AuthenticatedRequest_NoNameIdentifierClaim_FallsBackToIdentityName()
    {
        var identity = new ClaimsIdentity([], authenticationType: "Test", nameType: ClaimTypes.Name, roleType: null);
        identity.AddClaim(new Claim(ClaimTypes.Name, "jdoe"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        RateLimitPartition<string> partition = RateLimitPartitioner.CreatePartition(context, Options);

        Assert.Equal("user:jdoe", partition.PartitionKey);
    }
}
