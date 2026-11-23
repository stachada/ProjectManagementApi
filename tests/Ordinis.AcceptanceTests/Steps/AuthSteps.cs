using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Real sign-ins through <c>/auth/login</c> and refreshes through <c>/auth/refresh</c>. A persona
/// who "has signed in" keeps the issued token pair, and later requests as that persona present the
/// access token — so scenarios can test what happens to a session that already exists (e.g. after
/// the user is deactivated).
/// </summary>
[Binding]
internal sealed class AuthSteps(World world, Seeder seeder, ApiDriver api)
{
    /// <summary>Slug of the organization of the most recent sign-in attempt.</summary>
    private string? _lastSignInSlug;

    [Given(@"^(\w+) has signed in to ""([^""]+)""$")]
    public async Task GivenPersonaHasSignedIn(string persona, string organization)
    {
        ApiResponse response = await SignInAsync(persona, organization, world.PersonaPasswords[persona]);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not sign in: {response}");
        KeepSession(persona, response);
    }

    [When(@"^(\w+) signs in to ""([^""]+)"" with password ""([^""]+)""$")]
    public Task WhenPersonaSignsIn(string persona, string organization, string password) =>
        SignInAsync(persona, organization, password);

    [Then(@"^(\w+) can sign in to ""([^""]+)"" with ""([^""]+)"" and ""([^""]+)""$")]
    public async Task ThenPersonaCanSignIn(string persona, string organization, string email, string password)
    {
        string slug = (await seeder.FindOrganizationAsync(world.OrganizationNamed(organization))).Slug;
        ApiResponse response = await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login",
            new { organizationSlug = slug, email, password });
        Assert.True(response.IsSuccess, $"{persona} could not sign in: {response}");
    }

    /// <summary>
    /// Exchanges the persona's refresh token for a new session. A persona who has not signed in
    /// yet signs in first: every client session starts with a sign-in.
    /// </summary>
    [When(@"^(\w+) refreshes the session$")]
    public async Task WhenPersonaRefreshesTheSession(string persona)
    {
        if (!world.RefreshTokens.TryGetValue(persona, out string? refreshToken))
        {
            string organization = world.Organizations.Single(o => o.Value == world.PersonaNamed(persona).OrganizationId).Key;
            await GivenPersonaHasSignedIn(persona, organization);
            refreshToken = world.RefreshTokens[persona];
        }

        ApiResponse response = await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/refresh", new { refreshToken });

        if (response.IsSuccess)
        {
            KeepSession(persona, response);
        }
    }

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

    /// <summary>
    /// The rejection must be indistinguishable from signing in with an unknown email: the same
    /// status, type, title and detail, and no wording that hints at the account's state.
    /// </summary>
    [Then(@"^the sign-in error does not reveal that the account is inactive$")]
    public async Task ThenTheSignInErrorDoesNotRevealInactivity()
    {
        ApiResponse rejected = world.Response;
        foreach (string hint in new[] { "inactive", "deactivat", "disabled" })
        {
            Assert.DoesNotContain(hint, rejected.Body, StringComparison.OrdinalIgnoreCase);
        }

        ApiResponse unknownEmail = await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login",
            new { organizationSlug = _lastSignInSlug, email = "nobody@unknown.example", password = "wrong-password-1" });
        world.LastResponse = rejected;

        Assert.Equal(unknownEmail.StatusCode, rejected.StatusCode);
        foreach (string property in new[] { "type", "title", "detail" })
        {
            Assert.Equal(PropertyOf(unknownEmail, property), PropertyOf(rejected, property));
        }
    }

    private async Task<ApiResponse> SignInAsync(string persona, string organization, string password)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        string slug = (await seeder.FindOrganizationAsync(organizationId)).Slug;
        string email = (await seeder.FindUserAsync(world.PersonaNamed(persona).UserId)).Email;
        _lastSignInSlug = slug;

        return await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login",
            new { organizationSlug = slug, email, password });
    }

    private void KeepSession(string persona, ApiResponse response)
    {
        world.SessionTokens[persona] = response.Json.GetProperty("accessToken").GetString()!;
        world.RefreshTokens[persona] = response.Json.GetProperty("refreshToken").GetString()!;
    }

    private static string? PropertyOf(ApiResponse response, string property) =>
        response.Json.TryGetProperty(property, out System.Text.Json.JsonElement value) ? value.ToString() : null;
}
