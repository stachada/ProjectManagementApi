using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Organizations.Requests;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Exercises <c>IdempotencyMiddleware</c> end-to-end against <c>POST /api/v1/organizations</c> —
/// one of the eight <c>[Idempotent]</c>-marked routes, chosen because it needs no parent-entity
/// seeding. See <c>docs/API_INFRASTRUCTURE.md#idempotencymiddleware</c> for the full mechanism.
/// </summary>
public sealed class IdempotencyMiddlewareTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Post_SameKeySameBody_ReplaysIdenticalResponseAndCreatesOnlyOneResource()
    {
        string idempotencyKey = Guid.CreateVersion7().ToString();
        var request = new CreateOrganizationRequest("Replay Co", "Created once, replayed once.");

        HttpResponseMessage first = await SendWithIdempotencyKeyAsync(request, idempotencyKey);
        HttpResponseMessage second = await SendWithIdempotencyKeyAsync(request, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        OrganizationDto? firstDto = await first.Content.ReadFromJsonAsync<OrganizationDto>();
        OrganizationDto? secondDto = await second.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(firstDto);
        Assert.NotNull(secondDto);
        Assert.Equal(firstDto.Id, secondDto.Id);

        int count = await SeedAsync(db => db.Organizations.CountAsync(o => o.Name == "Replay Co"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Post_SameKeyDifferentBody_Returns409Conflict()
    {
        string idempotencyKey = Guid.CreateVersion7().ToString();

        HttpResponseMessage first = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest("Conflict Co A", "First body."), idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        HttpResponseMessage second = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest("Conflict Co B", "Different body, same key."), idempotencyKey);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_NoIdempotencyKey_CreatesTwoIndependentResources()
    {
        var request = new CreateOrganizationRequest("No Key Co", "No idempotency key sent.");

        HttpResponseMessage first = await Client.PostAsJsonAsync("/api/v1/organizations", request);
        HttpResponseMessage second = await Client.PostAsJsonAsync("/api/v1/organizations", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // No Idempotency-Key means no replay guard, but CreateOrganizationValidator still rejects
        // a duplicate slug derived from the same Name - proves this is the *validator* rejecting
        // it (422), not the idempotency middleware replaying or conflicting (201/409).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task Post_FailedRequestThenRetryWithSameKey_SecondCallSucceeds()
    {
        string idempotencyKey = Guid.CreateVersion7().ToString();

        HttpResponseMessage failed = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest(string.Empty, "Invalid - empty name."), idempotencyKey);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);

        HttpResponseMessage retried = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest("Retried Co", "Valid on retry, same key."), idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }

    private async Task<HttpResponseMessage> SendWithIdempotencyKeyAsync(CreateOrganizationRequest body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organizations")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return await Client.SendAsync(request);
    }
}
