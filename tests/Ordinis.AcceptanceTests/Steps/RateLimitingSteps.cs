using System.Net;
using System.Net.Http.Headers;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Rate limiting, against the <see cref="TestHost.RateLimited"/> host (the production limiter, which
/// the main test host switches off). Every scenario gets its own client address, and its users are
/// new, so no scenario inherits another's quota.
/// </summary>
/// <remarks>
/// Scenarios in which a window has to pass (<c>@needs-clock</c>) run against
/// <see cref="TestHost.RateLimitedShortWindows"/> instead, where every window is
/// <see cref="TestHost.RateLimitWindowCompression"/> times shorter and the steps really wait:
/// the built-in limiters cannot be driven by the <see cref="TestHost.Clock"/>.
/// </remarks>
[Binding]
internal sealed class RateLimitingSteps(World world, Seeder seeder, ApiDriver api, ScenarioContext scenario)
{
    /// <summary>Slack on top of a window, for the limiter's replenishment tick (every 100 ms).</summary>
    private static readonly TimeSpan ReplenishmentSlack = TimeSpan.FromMilliseconds(500);

    /// <summary>Requests sent at once while using up a quota (in-process, so cheap).</summary>
    private const int Parallelism = 25;

    private static int _nextAddress;

    [BeforeScenario("needs-rate-limiter")]
    public void UseTheRateLimitedHost()
    {
        world.Host = scenario.ScenarioInfo.CombinedTags.Contains("needs-clock")
            ? TestHost.RateLimitedShortWindows
            : TestHost.RateLimited;
        world.ClientAddress = NewAddress();
    }

    // ---- Using up a quota ----

    [Given(@"^(\d+) anonymous requests have been made from one address within the current minute$")]
    public Task GivenAnonymousRequestsWereMade(int count) =>
        SendManyAsync(count, accessToken: null, varyAddress: false, AnonymousRequest);

    [Given(@"^(\d+) sign-in attempts have been made from one address within a minute$")]
    public Task GivenSignInAttemptsWereMade(int count) =>
        SendManyAsync(count, accessToken: null, varyAddress: false, SignInAttempt);

    [Given(@"^(\w+) has made (\d+) requests within the last minute( from several addresses|)$")]
    public async Task GivenPersonaHasMadeRequests(string persona, int count, string addresses)
    {
        string token = await seeder.AccessTokenForAsync(world.PersonaNamed(persona).UserId);
        Guid organizationId = world.PersonaNamed(persona).OrganizationId;
        await SendManyAsync(count, token, varyAddress: addresses.Length > 0, () => OrganizationRequest(organizationId));
    }

    [Given(@"^(\w+) made (\d+) requests within one 10-second span$")]
    public Task GivenPersonaMadeRequestsWithinOneSpan(string persona, int count) =>
        GivenPersonaHasMadeRequests(persona, count, addresses: string.Empty);

    // ---- Time passing (on the short-window host, where a "minute" lasts 6 seconds) ----

    [Given(@"^the next minute has started$")]
    public Task GivenTheNextMinuteHasStarted() => WaitAsync(TimeSpan.FromMinutes(1));

    [Given(@"^(\d+) seconds have passed since that span$")]
    public Task GivenSecondsHavePassed(int seconds) => WaitAsync(TimeSpan.FromSeconds(seconds));

    [When(@"^the health check is called (\d+) times within a minute from one address$")]
    public async Task WhenTheHealthCheckIsCalledRepeatedly(int count)
    {
        world.ConcurrentResponses.AddRange(await SendManyAsync(count, accessToken: null, varyAddress: false,
            () => new HttpRequestMessage(HttpMethod.Get, "/health"), assertNotLimited: false));
    }

    // ---- The next request ----

    [When(@"^another anonymous request is made from that address$")]
    public Task WhenAnotherAnonymousRequestIsMade() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Get, $"/api/v1/organizations/{Guid.CreateVersion7()}");

    [When(@"^an anonymous request is made from a different address$")]
    public Task WhenAnAnonymousRequestIsMadeFromADifferentAddress()
    {
        world.ClientAddress = NewAddress();
        return WhenAnotherAnonymousRequestIsMade();
    }

    [When(@"^another sign-in attempt is made from that address$")]
    public Task WhenAnotherSignInAttemptIsMade() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login", SignInBody);

    [When(@"^(\w+) makes (?:another|a) request(?: from the same address)?$")]
    public Task WhenPersonaMakesARequest(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/organizations/{world.PersonaNamed(persona).OrganizationId}");

    [When(@"^(\w+) makes another request from a new address$")]
    public Task WhenPersonaMakesARequestFromANewAddress(string persona)
    {
        world.ClientAddress = NewAddress();
        return WhenPersonaMakesARequest(persona);
    }

    [When(@"^the health check is called from that address$")]
    public Task WhenTheHealthCheckIsCalledFromThatAddress() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Get, "/health");

    // ---- Assertions ----

    [Then(@"^the request is not rate limited$")]
    public void ThenTheRequestIsNotRateLimited() =>
        Assert.True(world.Response.StatusCode != HttpStatusCode.TooManyRequests, world.Response.ToString());

    [Then(@"^none of the requests is rate limited$")]
    public void ThenNoneOfTheRequestsIsRateLimited()
    {
        Assert.NotEmpty(world.ConcurrentResponses);
        Assert.All(world.ConcurrentResponses, r => Assert.True(r.StatusCode != HttpStatusCode.TooManyRequests, r.ToString()));
    }

    [Then(@"^the caller is told (?:how many seconds to wait before retrying|when to retry)$")]
    public void ThenTheCallerIsToldWhenToRetry()
    {
        string? retryAfter = world.Response.Header("Retry-After");
        Assert.True(int.TryParse(retryAfter, out int seconds) && seconds > 0,
            $"Expected Retry-After to be a number of seconds, got '{retryAfter}': {world.Response}");
    }

    // ---- Helpers ----

    /// <summary>
    /// Really waits <paramref name="duration"/>, compressed like the windows, measured from now:
    /// the quota was used up just before, so at least that much has passed since it started.
    /// </summary>
    private Task WaitAsync(TimeSpan duration)
    {
        Assert.True(world.Host == TestHost.RateLimitedShortWindows,
            "Time can only pass for the rate limiter in a @needs-clock scenario (on the short-window host).");
        return Task.Delay((duration / TestHost.RateLimitWindowCompression) + ReplenishmentSlack);
    }

    private static readonly object SignInBody = new { organizationSlug = "acme", email = "nobody@acme.example", password = "wrong-password-1" };

    /// <summary>A documentation-range (TEST-NET-3) address no other scenario of this run uses.</summary>
    private static string NewAddress()
    {
        int n = Interlocked.Increment(ref _nextAddress);
        return $"203.0.{113 + (n / 250)}.{(n % 250) + 1}";
    }

    private static HttpRequestMessage AnonymousRequest() =>
        new(HttpMethod.Get, $"/api/v1/organizations/{Guid.CreateVersion7()}");

    private static HttpRequestMessage SignInAttempt() =>
        new(HttpMethod.Post, "/auth/login") { Content = System.Net.Http.Json.JsonContent.Create(SignInBody) };

    private static HttpRequestMessage OrganizationRequest(Guid organizationId) =>
        new(HttpMethod.Get, $"/api/v1/organizations/{organizationId}");

    /// <summary>
    /// Sends <paramref name="count"/> requests, <see cref="Parallelism"/> at a time, from the
    /// scenario's address (or each from a new one), none of which may be throttled yet.
    /// </summary>
    private async Task<List<ApiResponse>> SendManyAsync(
        int count, string? accessToken, bool varyAddress, Func<HttpRequestMessage> request, bool assertNotLimited = true)
    {
        using HttpClient client = world.Host!.CreateClient();
        var responses = new List<ApiResponse>(count);

        for (int sent = 0; sent < count; sent += Parallelism)
        {
            IEnumerable<Task<HttpStatusCode>> batch = Enumerable.Range(0, Math.Min(Parallelism, count - sent)).Select(async _ =>
            {
                using HttpRequestMessage message = request();
                message.Headers.Add(TestHost.ClientAddressHeader, varyAddress ? NewAddress() : world.ClientAddress);
                if (accessToken is not null)
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }

                using HttpResponseMessage response = await client.SendAsync(message);
                return response.StatusCode;
            });

            foreach (HttpStatusCode status in await Task.WhenAll(batch))
            {
                responses.Add(new ApiResponse(status, string.Empty, null, null, null, new Dictionary<string, string>(), string.Empty));
            }
        }

        if (assertNotLimited)
        {
            Assert.True(responses.All(r => r.StatusCode != HttpStatusCode.TooManyRequests),
                "Arrangement failed — the quota ran out before it was used up as described.");
        }

        return responses;
    }
}
