using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Real sign-ins through <c>/auth/login</c>. A persona who "has signed in" keeps the issued access
/// token, and later requests as that persona present it — so scenarios can test what happens to a
/// session that already exists (e.g. after the user is deactivated).
/// </summary>
[Binding]
internal sealed class AuthSteps(World world, Seeder seeder, ApiDriver api)
{
    [Given(@"^(\w+) has signed in to ""([^""]+)""$")]
    public async Task GivenPersonaHasSignedIn(string persona, string organization)
    {
        ApiResponse response = await SignInAsync(persona, organization, world.PersonaPasswords[persona]);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not sign in: {response}");
        world.SessionTokens[persona] = response.Json.GetProperty("accessToken").GetString()!;
    }

    [When(@"^(\w+) signs in to ""([^""]+)"" with password ""([^""]+)""$")]
    public Task WhenPersonaSignsIn(string persona, string organization, string password) =>
        SignInAsync(persona, organization, password);

    [Given(@"^(\w+) has deactivated the user (\w+)$")]
    public async Task GivenPersonaHasDeactivatedTheUser(string persona, string user)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post,
            $"/api/v1/users/{userId}/deactivate", body: null, await seeder.CurrentUserVersionAsync(userId));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not deactivate {user}: {response}");
    }

    [When(@"^(\w+) views the organization ""([^""]+)""$")]
    public Task WhenPersonaViewsTheOrganization(string persona, string organization) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/organizations/{world.OrganizationNamed(organization)}");

    private async Task<ApiResponse> SignInAsync(string persona, string organization, string password)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        string slug = (await seeder.FindOrganizationAsync(organizationId)).Slug;
        string email = (await seeder.FindUserAsync(world.PersonaNamed(persona).UserId)).Email;

        return await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login",
            new { organizationSlug = slug, email, password });
    }
}
