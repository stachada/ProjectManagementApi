using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Infrastructure.Security;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Real sign-ins through <c>/auth/login</c>, refreshes through <c>/auth/refresh</c> and sign-outs
/// through <c>/auth/logout</c>. A persona who "has signed in" keeps the issued token pair, and later
/// requests as that persona present the access token — so scenarios can test what happens to a
/// session that already exists (e.g. after the user is deactivated or signs out).
/// </summary>
/// <remarks>
/// Three tokens are remembered per persona: the <b>original</b> access token and the <b>kept</b>
/// refresh token from the first sign-in (what a client stored), and the <b>latest</b> refresh
/// token (in <see cref="World.RefreshTokens"/>), which every successful refresh rotates.
/// </remarks>
[Binding]
internal sealed class AuthSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>Credentials for requests that are about a missing field, not about who signs in.</summary>
    private const string AnyEmail = "nora@acme.example";
    private const string AnyPassword = "nora-secret-1";

    private readonly Dictionary<string, string> _originalAccessTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keptRefreshTokens = new(StringComparer.Ordinal);

    /// <summary>Slug of the organization of the most recent sign-in attempt.</summary>
    private string? _lastSignInSlug;

    // ---- Signing in ----

    [Given(@"^(\w+) has signed in to ""([^""]+)""(?: and kept the refresh token)?$")]
    public async Task GivenPersonaHasSignedIn(string persona, string organization)
    {
        ApiResponse response = await SignInAsync(persona, organization, world.PersonaPasswords[persona]);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not sign in: {response}");
        KeepSession(persona, response);
    }

    [When(@"^(\w+) signs in to ""([^""]+)""(?: again)?$")]
    public async Task WhenPersonaSignsInAgain(string persona, string organization)
    {
        ApiResponse response = await SignInAsync(persona, organization, world.PersonaPasswords[persona]);
        if (response.IsSuccess)
        {
            KeepSession(persona, response);
        }
    }

    [When(@"^(\w+) signs in to ""([^""]+)"" with password ""([^""]+)""$")]
    public Task WhenPersonaSignsIn(string persona, string organization, string password) =>
        SignInAsync(persona, organization, password);

    [When(@"^(\w+) signs in to ""([^""]+)"" with (\w+)'s own email and password$")]
    public async Task WhenPersonaSignsInWithOwnCredentials(string persona, string organization, string owner)
    {
        string email = (await seeder.FindUserAsync(world.PersonaNamed(owner).UserId)).Email;
        await LoginAsync(await SlugOfAsync(organization), email, world.PersonaPasswords[owner]);
    }

    [When(@"^someone signs in to ""([^""]+)"" with ""([^""]+)"" and ""([^""]+)""$")]
    public Task WhenSomeoneSignsIn(string slug, string email, string password) =>
        LoginAsync(slug, email, password);

    [When(@"^someone signs in without an organization$")]
    public Task WhenSomeoneSignsInWithoutAnOrganization() =>
        PostLoginAsync(new Dictionary<string, object?> { ["email"] = AnyEmail, ["password"] = AnyPassword });

    [When(@"^someone signs in to ""([^""]+)"" without an email$")]
    public Task WhenSomeoneSignsInWithoutAnEmail(string slug) =>
        PostLoginAsync(new Dictionary<string, object?> { ["organizationSlug"] = slug, ["password"] = AnyPassword });

    [When(@"^someone signs in to ""([^""]+)"" with email ""([^""]+)"" and ""([^""]+)""$")]
    public Task WhenSomeoneSignsInWithAMalformedEmail(string slug, string email, string password) =>
        LoginAsync(slug, email, password);

    [When(@"^someone signs in to ""([^""]+)"" as ""([^""]+)"" without a password$")]
    public Task WhenSomeoneSignsInWithoutAPassword(string slug, string email) =>
        PostLoginAsync(new Dictionary<string, object?> { ["organizationSlug"] = slug, ["email"] = email });

    // ---- Refreshing ----

    /// <summary>
    /// Exchanges the persona's latest refresh token for a new session. A persona who has not
    /// signed in yet signs in first: every client session starts with a sign-in.
    /// </summary>
    [When(@"^(\w+) refreshes the session$")]
    public async Task WhenPersonaRefreshesTheSession(string persona)
    {
        if (!world.RefreshTokens.ContainsKey(persona))
        {
            string organization = world.Organizations.First(o => o.Value == world.PersonaNamed(persona).OrganizationId).Key;
            await GivenPersonaHasSignedIn(persona, organization);
        }

        await RefreshAsync(persona, world.RefreshTokens[persona]);
    }

    [When(@"^(\w+) refreshes the session with the (kept|latest) refresh token$")]
    public Task WhenPersonaRefreshesWith(string persona, string which) =>
        RefreshAsync(persona, which == "kept" ? _keptRefreshTokens[persona] : world.RefreshTokens[persona]);

    [Given(@"^(\w+) has refreshed the session with the kept refresh token$")]
    public async Task GivenPersonaHasRefreshed(string persona)
    {
        ApiResponse response = await RefreshAsync(persona, _keptRefreshTokens[persona]);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not refresh the session: {response}");
    }

    [When(@"^someone refreshes a session with a made-up refresh token$")]
    public Task WhenSomeoneRefreshesWithAMadeUpToken() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/refresh",
            new { refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) });

    [When(@"^someone refreshes a session without a refresh token$")]
    public Task WhenSomeoneRefreshesWithoutAToken() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/refresh", new Dictionary<string, object?>());

    // ---- Signing out ----

    [When("^" + Actor + @" signs out$")]
    public Task WhenPersonaSignsOut(string persona) =>
        api.SendAsync(persona, HttpMethod.Post, "/auth/logout");

    [Given(@"^(\w+) has signed out$")]
    public async Task GivenPersonaHasSignedOut(string persona)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/auth/logout");
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not sign out: {response}");
    }

    // ---- Using access tokens ----

    [When(@"^(\w+) views the organization ""([^""]+)""$")]
    public Task WhenPersonaViewsTheOrganization(string persona, string organization) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/organizations/{world.OrganizationNamed(organization)}");

    [When(@"^(\w+) views the organization ""([^""]+)"" using the (?:original )?access token$")]
    public Task WhenPersonaViewsTheOrganizationWithTheOriginalToken(string persona, string organization) =>
        api.SendWithTokenAsync(_originalAccessTokens[persona], HttpMethod.Get, $"/api/v1/organizations/{world.OrganizationNamed(organization)}");

    /// <summary>
    /// A token for a real user of the organization, broken in exactly one way, so the rejection can
    /// only be caused by that defect.
    /// </summary>
    [When(@"^someone views the organization ""([^""]+)"" using (no access token|a malformed access token|an access token signed with a different key|an access token from a different issuer|an access token for a different audience)$")]
    public async Task WhenSomeoneViewsTheOrganizationUsingAnInvalidToken(string organization, string token)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        JwtOptions options = TestHost.Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        World.Persona user = world.Personas.Values.First(p => p.OrganizationId == organizationId);

        string? accessToken = token switch
        {
            "no access token" => null,
            "a malformed access token" => "not-a-jwt",
            "an access token signed with a different key" =>
                await ForgeTokenAsync(user.UserId, options.Issuer, options.Audience, RandomNumberGenerator.GetBytes(32)),
            "an access token from a different issuer" =>
                await ForgeTokenAsync(user.UserId, "Someone.Else", options.Audience, Convert.FromBase64String(options.SigningKey)),
            _ => await ForgeTokenAsync(user.UserId, options.Issuer, "Someone.Else.Api", Convert.FromBase64String(options.SigningKey)),
        };

        await api.SendWithTokenAsync(accessToken, HttpMethod.Get, $"/api/v1/organizations/{organizationId}");
    }

    // ---- Deactivation ----

    [Given(@"^(\w+) has deactivated the user (\w+)$")]
    public async Task GivenPersonaHasDeactivatedTheUser(string persona, string user)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post,
            $"/api/v1/users/{userId}/deactivate", body: null, await seeder.CurrentUserVersionAsync(userId));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not deactivate {user}: {response}");
    }

    // ---- Assertions ----

    [Then(@"^(\w+) can sign in to ""([^""]+)"" with ""([^""]+)"" and ""([^""]+)""$")]
    public async Task ThenPersonaCanSignIn(string persona, string organization, string email, string password)
    {
        ApiResponse response = await LoginAsync(await SlugOfAsync(organization), email, password);
        Assert.True(response.IsSuccess, $"{persona} could not sign in: {response}");
    }

    [Then(@"^a session for (\w+) is returned$")]
    public void ThenASessionIsReturned(string persona)
    {
        JsonElement session = SuccessfulJson();
        Assert.False(string.IsNullOrEmpty(session.GetProperty("refreshToken").GetString()), world.Response.ToString());
        Assert.Equal(world.PersonaNamed(persona).UserId.ToString(), ClaimOf(AccessToken(), "sub"));
    }

    [Then(@"^the access token expires (\d+) minutes from now$")]
    public void ThenTheAccessTokenExpires(int minutes)
    {
        DateTimeOffset expected = TestHost.Clock.GetUtcNow().AddMinutes(minutes);
        DateTimeOffset stated = SuccessfulJson().GetProperty("accessTokenExpiresAt").GetDateTimeOffset();
        DateTimeOffset claimed = DateTimeOffset.FromUnixTimeSeconds(long.Parse(ClaimOf(AccessToken(), "exp")!, System.Globalization.CultureInfo.InvariantCulture));

        Assert.InRange(stated, expected.AddMinutes(-1), expected.AddMinutes(1));
        Assert.InRange(claimed, stated.AddSeconds(-1), stated.AddSeconds(1));
    }

    [Then(@"^the access token identifies the user (\w+) with email, display name, organization ""([^""]+)"" and org role (\w+)$")]
    public async Task ThenTheAccessTokenIdentifiesTheUser(string persona, string organization, string orgRole)
    {
        string token = AccessToken();
        Ordinis.Domain.Users.User user = await seeder.FindUserAsync(world.PersonaNamed(persona).UserId);

        Assert.Equal(user.Id.ToString(), ClaimOf(token, "sub"));
        Assert.Equal(user.Email, ClaimOf(token, "email"));
        Assert.Equal(user.DisplayName, ClaimOf(token, "name"));
        Assert.Equal(world.OrganizationNamed(organization).ToString(), ClaimOf(token, "org_id"));
        Assert.Equal(orgRole, ClaimOf(token, "role"));
    }

    [Then(@"^the earlier refresh token no longer works$")]
    public async Task ThenTheEarlierRefreshTokenNoLongerWorks()
    {
        string persona = _keptRefreshTokens.Keys.Single();
        await AssertRefreshIsRejectedAsync(_keptRefreshTokens[persona]);
    }

    [Then(@"^(\w+) cannot refresh the session with the kept refresh token$")]
    public Task ThenPersonaCannotRefresh(string persona) =>
        AssertRefreshIsRejectedAsync(_keptRefreshTokens[persona]);

    [Then(@"^a new access token and a new refresh token are returned$")]
    public void ThenANewTokenPairIsReturned()
    {
        JsonElement session = SuccessfulJson();
        string persona = _keptRefreshTokens.Keys.Single();
        Assert.False(string.IsNullOrEmpty(session.GetProperty("refreshToken").GetString()), world.Response.ToString());
        Assert.NotEqual(_originalAccessTokens[persona], AccessToken());
    }

    [Then(@"^the new refresh token differs from the kept one$")]
    public void ThenTheNewRefreshTokenDiffers()
    {
        string persona = _keptRefreshTokens.Keys.Single();
        Assert.NotEqual(_keptRefreshTokens[persona], SuccessfulJson().GetProperty("refreshToken").GetString());
    }

    [Then(@"^the new access token carries org role (\w+)$")]
    public void ThenTheNewAccessTokenCarriesTheRole(string orgRole)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal(orgRole, ClaimOf(AccessToken(), "role"));
    }

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

        ApiResponse unknownEmail = await LoginAsync(_lastSignInSlug!, "nobody@unknown.example", "wrong-password-1");
        world.LastResponse = rejected;

        Assert.Equal(unknownEmail.StatusCode, rejected.StatusCode);
        foreach (string property in new[] { "type", "title", "detail" })
        {
            Assert.Equal(PropertyOf(unknownEmail, property), PropertyOf(rejected, property));
        }
    }

    // ---- Helpers ----

    private async Task<ApiResponse> SignInAsync(string persona, string organization, string password)
    {
        string email = (await seeder.FindUserAsync(world.PersonaNamed(persona).UserId)).Email;
        return await LoginAsync(await SlugOfAsync(organization), email, password);
    }

    private Task<ApiResponse> LoginAsync(string slug, string email, string password) =>
        PostLoginAsync(new Dictionary<string, object?> { ["organizationSlug"] = slug, ["email"] = email, ["password"] = password });

    /// <summary>The body is a dictionary so that "without …" can leave a field out entirely.</summary>
    private Task<ApiResponse> PostLoginAsync(Dictionary<string, object?> body)
    {
        _lastSignInSlug = body.TryGetValue("organizationSlug", out object? slug) ? slug as string : null;
        return api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login", body);
    }

    private async Task<ApiResponse> RefreshAsync(string persona, string refreshToken)
    {
        ApiResponse response = await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/refresh", new { refreshToken });
        if (response.IsSuccess)
        {
            KeepSession(persona, response);
        }

        return response;
    }

    private async Task AssertRefreshIsRejectedAsync(string refreshToken)
    {
        ApiResponse previous = world.Response;
        ApiResponse response = await api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/refresh", new { refreshToken });
        world.LastResponse = previous;
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Unauthorized, $"Expected the refresh to be rejected, got {response}");
    }

    /// <summary>The latest session becomes the current one; the first one is also kept as the original.</summary>
    private void KeepSession(string persona, ApiResponse response)
    {
        string accessToken = response.Json.GetProperty("accessToken").GetString()!;
        string refreshToken = response.Json.GetProperty("refreshToken").GetString()!;

        world.SessionTokens[persona] = accessToken;
        world.RefreshTokens[persona] = refreshToken;
        _originalAccessTokens.TryAdd(persona, accessToken);
        _keptRefreshTokens.TryAdd(persona, refreshToken);
    }

    private async Task<string> SlugOfAsync(string organization) =>
        (await seeder.FindOrganizationAsync(world.OrganizationNamed(organization))).Slug;

    private async Task<string> ForgeTokenAsync(Guid userId, string issuer, string audience, byte[] key)
    {
        Ordinis.Domain.Users.User user = await seeder.FindUserAsync(userId);
        DateTime now = TestHost.Clock.GetUtcNow().UtcDateTime;

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim("org_id", user.OrganizationId.ToString()),
                new Claim(ClaimTypes.Role, user.OrgRole.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            ],
            notBefore: now,
            expires: now.AddMinutes(15),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private JsonElement SuccessfulJson()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        return world.Response.Json;
    }

    private string AccessToken() => SuccessfulJson().GetProperty("accessToken").GetString()!;

    /// <summary>A claim from a JWT's payload, read without validating the token.</summary>
    private static string? ClaimOf(string jwt, string claim)
    {
        string payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        // The API writes the role under its full claim-type URI (ClaimTypes.Role), not "role".
        string name = claim == "role" && !document.RootElement.TryGetProperty("role", out _) ? ClaimTypes.Role : claim;
        return document.RootElement.TryGetProperty(name, out JsonElement value) ? value.ToString() : null;
    }

    private static string? PropertyOf(ApiResponse response, string property) =>
        response.Json.TryGetProperty(property, out JsonElement value) ? value.ToString() : null;
}
