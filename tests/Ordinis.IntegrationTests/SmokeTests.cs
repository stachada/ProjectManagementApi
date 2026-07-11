using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests;

/// <summary>
/// Verifies the Phase 9 integration-test infrastructure itself (container boot, migrations,
/// rate-limiter override) rather than any single feature — delete once real controller tests
/// exist and provide the same coverage incidentally.
/// </summary>
public sealed class SmokeTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        HttpResponseMessage response = await Client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RateLimiter_IsDisabledForTests_AllowsManyRequestsInARow()
    {
        for (int i = 0; i < 150; i++)
        {
            HttpResponseMessage response = await Client.GetAsync("/health");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }
}
