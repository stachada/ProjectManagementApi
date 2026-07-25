using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Exercises the real rate-limiting pipeline end-to-end (<c>ApiServiceExtensions.AddApiServices</c>
/// + <c>app.UseRateLimiter()</c> in <c>Program.cs</c>). <see cref="OrdinisApiFactory"/>'s shared
/// client has the global limiter disabled (see its <c>ConfigureWebHost</c> remarks) so ordinary
/// tests aren't throttled - this class builds its own low-limit client via
/// <c>PostConfigure&lt;RateLimiterOptions&gt;</c>, which runs after every other
/// <c>Configure&lt;RateLimiterOptions&gt;</c> call (including the disabling one) regardless of
/// registration order, so it deterministically wins.
/// </summary>
public sealed class RateLimitingTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    private const int PermitLimit = 3;

    private HttpClient CreateThrottledClient() => Factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimiterOptions>(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: "test-partition",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PermitLimit,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }));
            })))
        .CreateClient();

    [Fact]
    public async Task RequestsWithinLimit_Succeed()
    {
        using HttpClient client = CreateThrottledClient();

        for (int i = 0; i < PermitLimit; i++)
        {
            HttpResponseMessage response = await client.GetAsync($"/api/v1/organizations/{Guid.CreateVersion7()}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task RequestExceedingLimit_Returns429WithRetryAfterAndProblemDetailsBody()
    {
        using HttpClient client = CreateThrottledClient();

        for (int i = 0; i < PermitLimit; i++)
        {
            await client.GetAsync($"/api/v1/organizations/{Guid.CreateVersion7()}");
        }

        HttpResponseMessage response = await client.GetAsync($"/api/v1/organizations/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter is not null, "Expected a Retry-After header on a 429 response.");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.True(problem.Extensions.ContainsKey("correlationId"));
    }

    [Fact]
    public async Task HealthEndpoint_IsExemptFromRateLimiting()
    {
        using HttpClient client = CreateThrottledClient();

        for (int i = 0; i < PermitLimit + 2; i++)
        {
            HttpResponseMessage response = await client.GetAsync("/health");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
