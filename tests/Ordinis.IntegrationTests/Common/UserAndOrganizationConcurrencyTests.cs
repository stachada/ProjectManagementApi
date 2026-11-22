using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Organizations.Requests;
using Ordinis.Api.Users.Requests;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Regression for GAP-12: users and organizations use the same end-to-end optimistic concurrency
/// as every other aggregate — an <c>ETag</c> on <c>GET</c>, a required <c>If-Match</c> on writes
/// (missing → 422, stale → 409) — and session bookkeeping (refresh-token rotation on every
/// sign-in / refresh) does not change a user's version.
/// </summary>
public sealed class UserAndOrganizationConcurrencyTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetUserAndOrganization_ReturnETagMatchingRowVersion()
    {
        (Guid organizationId, Guid adminId) = await AuthenticateAsOrgAdminAsync();

        HttpResponseMessage user = await Client.GetAsync($"/api/v1/users/{adminId}");
        HttpResponseMessage organization = await Client.GetAsync($"/api/v1/organizations/{organizationId}");

        Assert.Equal(QuotedBase64(await SeedAsync(db => db.Users.Where(u => u.Id == adminId).Select(u => u.RowVersion).SingleAsync())), user.Headers.ETag?.Tag);
        Assert.Equal(QuotedBase64(await SeedAsync(db => db.Organizations.Where(o => o.Id == organizationId).Select(o => o.RowVersion).SingleAsync())), organization.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task UserWrite_WithoutIfMatch_Returns422()
    {
        (_, Guid adminId) = await AuthenticateAsOrgAdminAsync();

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{adminId}", new UpdateUserRequest("Renamed"));

        await AssertValidationProblemAsync(response, "IfMatch", "If-Match header is required.");
    }

    [Fact]
    public async Task OrganizationWrite_WithoutIfMatch_Returns422()
    {
        (Guid organizationId, _) = await AuthenticateAsOrgAdminAsync();

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{organizationId}/suspend", content: null);

        await AssertValidationProblemAsync(response, "IfMatch", "If-Match header is required.");
        Assert.True(await SeedAsync(db => db.Organizations.AnyAsync(o => o.Id == organizationId && o.IsActive)));
    }

    [Fact]
    public async Task UserWrite_WithStaleIfMatch_Returns409AndKeepsTheOtherChange()
    {
        (_, Guid adminId) = await AuthenticateAsOrgAdminAsync();
        string staleETag = (await Client.GetAsync($"/api/v1/users/{adminId}")).Headers.ETag!.Tag;
        HttpResponseMessage first = await SendWithCurrentUserVersionAsync(HttpMethod.Put, adminId, $"/api/v1/users/{adminId}", new UpdateUserRequest("First edit"));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{adminId}") { Content = JsonContent.Create(new UpdateUserRequest("Lost update")) };
        request.Headers.TryAddWithoutValidation("If-Match", staleETag);
        HttpResponseMessage second = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Concurrency conflict", (await second.Content.ReadFromJsonAsync<ProblemDetails>())?.Title);
        Assert.Equal("First edit", await SeedAsync(db => db.Users.Where(u => u.Id == adminId).Select(u => u.DisplayName).SingleAsync()));
    }

    [Fact]
    public async Task OrganizationWrite_WithStaleIfMatch_Returns409()
    {
        (Guid organizationId, _) = await AuthenticateAsOrgAdminAsync();
        byte[]? stale = await SeedAsync(db => db.Organizations.Where(o => o.Id == organizationId).Select(o => o.RowVersion).SingleAsync());
        HttpResponseMessage rename = await SendWithCurrentOrganizationVersionAsync(HttpMethod.Put, organizationId,
            $"/api/v1/organizations/{organizationId}", new UpdateOrganizationRequest("Renamed Co", null));
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        HttpResponseMessage suspend = await SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organizationId}/suspend", stale);

        Assert.Equal(HttpStatusCode.Conflict, suspend.StatusCode);
        Assert.True(await SeedAsync(db => db.Organizations.AnyAsync(o => o.Id == organizationId && o.IsActive)));
    }

    [Fact]
    public async Task UserVersion_ChangesOnRealChanges_ButNotOnSessionBookkeeping()
    {
        (_, Guid userId) = await SeedOrganizationWithUserAsync();
        (byte[]? initialVersion, DateTimeOffset initialUpdatedAt) = await ReadVersionAsync(userId);

        // What sign-in / refresh / logout do: rotate the stored refresh token.
        await SeedAsync(async db =>
        {
            User user = await db.Users.SingleAsync(u => u.Id == userId);
            user.SetRefreshToken("sha256:abc", DateTimeOffset.UtcNow.AddDays(7));
            return await db.SaveChangesAsync();
        });
        (byte[]? afterSession, DateTimeOffset afterSessionUpdatedAt) = await ReadVersionAsync(userId);

        // A change that is part of the representation.
        await SeedAsync(async db =>
        {
            User user = await db.Users.SingleAsync(u => u.Id == userId);
            user.ChangeOrgRole(Role.Viewer);
            return await db.SaveChangesAsync();
        });
        (byte[]? afterRoleChange, _) = await ReadVersionAsync(userId);

        Assert.Equal(initialVersion, afterSession);
        Assert.Equal(initialUpdatedAt, afterSessionUpdatedAt);
        Assert.NotEqual(afterSession, afterRoleChange);
        Assert.Equal("sha256:abc", await SeedAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.RefreshToken).SingleAsync()));
    }

    private Task<(byte[]? Version, DateTimeOffset UpdatedAt)> ReadVersionAsync(Guid userId) => SeedAsync(async db =>
    {
        var row = await db.Users.Where(u => u.Id == userId).Select(u => new { u.RowVersion, u.UpdatedAt }).SingleAsync();
        return (row.RowVersion, row.UpdatedAt);
    });

    private static string QuotedBase64(byte[]? version) => $"\"{Convert.ToBase64String(version!)}\"";
}
