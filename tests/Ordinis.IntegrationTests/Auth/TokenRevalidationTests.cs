using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Users.Requests;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Auth;

/// <summary>
/// Regression for GAP-06: a signature-valid access token must reflect the user's <b>current</b>
/// state on every request. Each test issues a real token first, then changes the user in the
/// database, then reuses the old token — exactly what a client holding a 15-minute token does.
/// </summary>
public sealed class TokenRevalidationTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task DeactivatedUser_ExistingToken_Returns401()
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();
        await AuthenticateAsUserAsync(userId);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/v1/organizations/{organizationId}")).StatusCode);

        await UpdateUserAsync(userId, u => u.Deactivate());

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{organizationId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ReactivatedUser_ExistingToken_WorksAgain()
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();
        await AuthenticateAsUserAsync(userId);
        await UpdateUserAsync(userId, u => u.Deactivate());
        await UpdateUserAsync(userId, u => u.Reactivate());

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{organizationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DemotedOrgAdmin_ExistingToken_LosesAdminAuthorityImmediately()
    {
        (Guid organizationId, Guid adminId) = await AuthenticateAsOrgAdminAsync();
        await UpdateUserAsync(adminId, u => u.ChangeOrgRole(Role.Member));

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{organizationId}/suspend", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await SeedAsync(db => db.Organizations.AnyAsync(o => o.Id == organizationId && o.IsActive)));
    }

    [Fact]
    public async Task PromotedUser_ExistingToken_GainsAdminAuthorityImmediately()
    {
        (_, Guid userId) = await SeedOrganizationWithUserAsync(orgRole: Role.Member);
        await AuthenticateAsUserAsync(userId);
        await UpdateUserAsync(userId, u => u.ChangeOrgRole(Role.Admin));

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("Dan Smith", "dan@example.com", "correct-horse-1", Role.Member));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task UpdateUserAsync(Guid userId, Action<User> change) => SeedAsync(async db =>
    {
        User user = await db.Users.SingleAsync(u => u.Id == userId);
        change(user);
        return await db.SaveChangesAsync();
    });
}
