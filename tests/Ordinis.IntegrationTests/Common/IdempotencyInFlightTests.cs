using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Api.Organizations.Requests;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Regression for GAP-13: two requests with the same <c>Idempotency-Key</c> must never both
/// execute. <see cref="ConcurrencyRaceInterceptor"/> parks the first request inside its
/// <c>SaveChangesAsync</c> — i.e. while it holds the key — so "a duplicate arrives while the
/// original is in flight" is deterministic rather than timing-dependent.
/// </summary>
public sealed class IdempotencyInFlightTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Post_DuplicateWhileOriginalInFlight_Returns409InProgressThenReplaysAfterCompletion()
    {
        await AuthenticateAsOrgAdminAsync();
        string key = Guid.CreateVersion7().ToString();
        var request = new CreateOrganizationRequest("In Flight Co", "Created exactly once.");
        ConcurrencyRaceInterceptor interceptor = Factory.Services.GetRequiredService<ConcurrencyRaceInterceptor>();
        interceptor.Arm();

        try
        {
            Task<HttpResponseMessage> original = SendWithKeyAsync(request, key);
            Task arrived = await Task.WhenAny(interceptor.WaitForFirstArrivalAsync(), original).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(arrived != original, "The original request finished before reaching SaveChangesAsync.");

            // Same key, same body, original still running → rejected, not executed.
            HttpResponseMessage duplicate = await SendWithKeyAsync(request, key);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("Request in progress", (await duplicate.Content.ReadFromJsonAsync<ProblemDetails>(ApiJson.Options))?.Title);
            Assert.NotNull(duplicate.Headers.RetryAfter);

            // Same key, different body → still the existing key-conflict error.
            HttpResponseMessage otherBody = await SendWithKeyAsync(new CreateOrganizationRequest("Other Co", null), key);
            Assert.Equal(HttpStatusCode.Conflict, otherBody.StatusCode);
            Assert.Equal("Idempotency key conflict", (await otherBody.Content.ReadFromJsonAsync<ProblemDetails>(ApiJson.Options))?.Title);

            interceptor.ReleaseFirst();
            HttpResponseMessage first = await original.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            // Retried after completion → the original response is replayed.
            HttpResponseMessage retry = await SendWithKeyAsync(request, key);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Equal(first.Headers.Location, retry.Headers.Location);
        }
        finally
        {
            interceptor.Disarm();
        }

        Assert.Equal(1, await SeedAsync(db => db.Organizations.CountAsync(o => o.Name == "In Flight Co")));
        Assert.Equal(0, await SeedAsync(db => db.Organizations.CountAsync(o => o.Name == "Other Co")));
    }

    [Fact]
    public async Task Post_ConcurrentIdenticalRequests_ExactlyOneExecutes()
    {
        await AuthenticateAsOrgAdminAsync();
        string key = Guid.CreateVersion7().ToString();
        var request = new CreateOrganizationRequest("Burst Co", "Ten identical requests at once.");

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => SendWithKeyAsync(request, key)));

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"Unexpected {(int)r.StatusCode} {r.StatusCode}"));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, await SeedAsync(db => db.Organizations.CountAsync(o => o.Name == "Burst Co")));
    }

    private Task<HttpResponseMessage> SendWithKeyAsync(CreateOrganizationRequest body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organizations") { Content = JsonContent.Create(body) };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return Client.SendAsync(message);
    }
}
