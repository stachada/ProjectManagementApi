using System.Globalization;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Users;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// User administration — creation, profile and org-role changes, deactivation — and read-backs
/// of a user's state. Users created through the API become personas, addressable both by their
/// display name ("Dan Smith") and by their first name ("Dan").
/// </summary>
[Binding]
internal sealed class UserSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>Password of users created by steps that do not state one.</summary>
    private const string NewUserPassword = "correct-horse-1";

    /// <summary>A role ordinal no <see cref="Role"/> member has.</summary>
    private const int UnknownEnumValue = 99;

    // ---- Arrangements ----

    [Given(@"^(\w+) has created a user ""([^""]+)"" with email ""([^""]+)""$")]
    public async Task GivenPersonaHasCreatedAUser(string persona, string displayName, string email)
    {
        ApiResponse response = await CreateAsync(persona, Body(displayName, email, NewUserPassword));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not create user '{displayName}': {response}");
    }

    [Given(@"^(\w+)'s email is ""([^""]+)""$")]
    public Task GivenPersonasEmailIs(string persona, string email) =>
        seeder.SetUserEmailAsync(world.PersonaNamed(persona).UserId, email);

    [Given(@"^(\w+) has read the user (\w+)$")]
    public async Task GivenPersonaHasReadTheUser(string persona, string user)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/users/{userId}");
        Assert.True(response.IsSuccess && response.ETag is not null, $"Arrangement failed — {persona} could not read user {user}: {response}");
        world.ReadVersions[(persona, userId)] = response.ETag;
    }

    [Given(@"^(\w+) has since changed the display name of (\w+) to ""([^""]+)""$")]
    public async Task GivenPersonaHasChangedTheDisplayName(string persona, string user, string displayName)
    {
        ApiResponse response = await UpdateAsync(persona, user, new { displayName }, suffix: string.Empty);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not rename {user}: {response}");
    }

    [Given(@"^(\w+) has changed the org role of (\w+) to (Viewer|Member|Admin)$")]
    public async Task GivenPersonaHasChangedTheOrgRole(string persona, string user, Role newOrgRole)
    {
        ApiResponse response = await ChangeOrgRoleAsync(persona, user, newOrgRole);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not change the org role of {user}: {response}");
    }

    [Given(@"^(\w+) has reactivated the user (\w+)$")]
    public async Task GivenPersonaHasReactivatedTheUser(string persona, string user)
    {
        ApiResponse response = await PostLifecycleAsync(persona, user, "reactivate");
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not reactivate {user}: {response}");
    }

    // ---- Creation ----

    [When("^" + Actor + @" creates a user ""([^""]+)"" with email ""([^""]+)""(?: in ""[^""]+"")?$")]
    public Task WhenPersonaCreatesAUser(string persona, string displayName, string email) =>
        CreateAsync(persona, Body(displayName, email, NewUserPassword));

    [When("^" + Actor + @" creates a user ""([^""]+)"" with email ""([^""]+)"" and org role (Viewer|Member|Admin)$")]
    public Task WhenPersonaCreatesAUserWithRole(string persona, string displayName, string email, Role orgRole)
    {
        Dictionary<string, object?> body = Body(displayName, email, NewUserPassword);
        body["orgRole"] = orgRole;
        return CreateAsync(persona, body);
    }

    [When("^" + Actor + @" creates a user ""([^""]+)"" with email ""([^""]+)"" and password ""([^""]+)""$")]
    public Task WhenPersonaCreatesAUserWithPassword(string persona, string displayName, string email, string password) =>
        CreateAsync(persona, Body(displayName, email, password));

    /// <summary>A two-column "field | value" table without a header row: display name, email, password.</summary>
    [When("^" + Actor + @" creates a user with:$")]
    public Task WhenPersonaCreatesAUserWith(string persona, DataTable table)
    {
        Dictionary<string, string> fields = TwoColumn(table);
        return CreateAsync(persona, Body(fields["display name"], fields["email"], fields["password"]));
    }

    /// <summary>One invalid detail on an otherwise valid user; "without …" leaves the field out entirely.</summary>
    [When("^" + Actor + @" creates a user (without a display name|with a \d+-character display name|without an email|with email ""[^""]+""|with a \d+-character email|without a password|with password ""[^""]+""|with an unknown org role)$")]
    public Task WhenPersonaCreatesAnInvalidUser(string persona, string variant)
    {
        Dictionary<string, object?> body = Body("Dan Smith", "dan@acme.example", NewUserPassword);

        switch (variant)
        {
            case "without a display name":
                body.Remove("displayName");
                break;
            case "without an email":
                body.Remove("email");
                break;
            case "without a password":
                body.Remove("password");
                break;
            case "with an unknown org role":
                body["orgRole"] = UnknownEnumValue;
                break;
            case not null when variant.StartsWith("with email ", StringComparison.Ordinal):
                body["email"] = variant.Split('"')[1];
                break;
            case not null when variant.StartsWith("with password ", StringComparison.Ordinal):
                body["password"] = variant.Split('"')[1];
                break;
            case not null when variant.EndsWith("character display name", StringComparison.Ordinal):
                body["displayName"] = new string('n', Length(variant));
                break;
            case not null when variant.EndsWith("character email", StringComparison.Ordinal):
                const string domain = "@acme.example";
                body["email"] = new string('e', Length(variant) - domain.Length) + domain;
                break;
            default:
                throw new InvalidOperationException($"Unknown user variant '{variant}'.");
        }

        return CreateAsync(persona, body);
    }

    // ---- Viewing ----

    [When("^" + Actor + @" views the user (\w+)$")]
    public Task WhenPersonaViewsTheUser(string persona, string user) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/users/{world.PersonaNamed(user).UserId}");

    [When("^" + Actor + @" views the user ""([^""]+)""$")]
    public Task WhenPersonaViewsTheNamedUser(string persona, string displayName) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/users/{world.PersonaNamed(displayName).UserId}");

    [When("^" + Actor + @" views an unknown user$")]
    public Task WhenPersonaViewsAnUnknownUser(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/users/{Guid.CreateVersion7()}");

    // ---- Profile and org role ----

    [When("^" + Actor + @" changes the display name of (\w+) to ""([^""]+)""" + VersionSuffix.Pattern + "$")]
    public Task WhenPersonaChangesTheDisplayName(string persona, string user, string displayName, string suffix) =>
        UpdateAsync(persona, user, new { displayName }, suffix);

    [When("^" + Actor + @" changes the display name of (\w+) to (a blank name|a \d+-character name)$")]
    public Task WhenPersonaChangesTheDisplayNameInvalidly(string persona, string user, string variant) =>
        UpdateAsync(persona, user,
            new { displayName = variant == "a blank name" ? "   " : new string('n', Length(variant)) },
            suffix: string.Empty);

    [When("^" + Actor + @" changes the org role of (\w+) to (Viewer|Member|Admin)$")]
    public Task WhenPersonaChangesTheOrgRole(string persona, string user, Role newOrgRole) =>
        ChangeOrgRoleAsync(persona, user, newOrgRole);

    [When("^" + Actor + @" changes the org role of (\w+) to an unknown role$")]
    public Task WhenPersonaChangesTheOrgRoleToAnUnknownRole(string persona, string user) =>
        ChangeOrgRoleAsync(persona, user, UnknownEnumValue);

    // ---- Deactivation ----

    [When("^" + Actor + @" deactivates the user (\w+)$")]
    public Task WhenPersonaDeactivatesTheUser(string persona, string user) =>
        PostLifecycleAsync(persona, user, "deactivate");

    [When("^" + Actor + @" reactivates the user (\w+)$")]
    public Task WhenPersonaReactivatesTheUser(string persona, string user) =>
        PostLifecycleAsync(persona, user, "reactivate");

    // ---- Assertions ----

    [Then(@"^the response points to the new user$")]
    public void ThenTheResponsePointsToTheNewUser()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Guid id = world.Response.Json.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/users/{id}", world.Response.Location?.AbsolutePath);
    }

    [Then(@"^the user ""([^""]+)"" belongs to ""([^""]+)""$")]
    public async Task ThenTheUserBelongsTo(string displayName, string organization)
    {
        User user = await seeder.FindUserAsync(world.PersonaNamed(displayName).UserId);
        Assert.Equal(world.OrganizationNamed(organization), user.OrganizationId);
    }

    [Then(@"^the user ""([^""]+)"" belongs to ""([^""]+)"" with org role (Viewer|Member|Admin)$")]
    public async Task ThenTheUserBelongsToWithRole(string displayName, string organization, Role orgRole)
    {
        User user = await seeder.FindUserAsync(world.PersonaNamed(displayName).UserId);
        Assert.Equal(world.OrganizationNamed(organization), user.OrganizationId);
        Assert.Equal(orgRole, user.OrgRole);
    }

    [Then(@"^the user ""([^""]+)"" is (active|inactive)$")]
    public Task ThenTheNamedUserIsActive(string displayName, string state) =>
        ThenTheUserIsActive(displayName, state);

    [Then(@"^the user (\w+) is (active|inactive)$")]
    public async Task ThenTheUserIsActive(string user, string state)
    {
        Assert.Equal(state == "active", (await seeder.FindUserAsync(world.PersonaNamed(user).UserId)).IsActive);
    }

    [Then(@"^the user (\w+) has org role (Viewer|Member|Admin)$")]
    public async Task ThenTheUserHasOrgRole(string user, Role expected)
    {
        Assert.Equal(expected, (await seeder.FindUserAsync(world.PersonaNamed(user).UserId)).OrgRole);
    }

    [Then(@"^the user (\w+) has display name ""([^""]+)""$")]
    public async Task ThenTheUserHasDisplayName(string user, string displayName)
    {
        Assert.Equal(displayName, (await seeder.FindUserAsync(world.PersonaNamed(user).UserId)).DisplayName);
    }

    [Then(@"^the user details show display name, email, organization, org role, active flag, creation and update times$")]
    public void ThenTheUserDetailsShowTheFields()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        JsonElement user = world.Response.Json;

        foreach (string field in new[] { "displayName", "email", "organizationId", "organizationName", "orgRole", "isActive", "createdAt", "updatedAt" })
        {
            Assert.True(user.TryGetProperty(field, out JsonElement value) && value.ValueKind != JsonValueKind.Null,
                $"Expected a '{field}' in the user details: {world.Response.Body}");
        }
    }

    /// <summary>
    /// No credential of any kind: no password (plain or hashed) and no access or refresh token.
    /// The concurrency token is a version stamp, not a credential, so it is allowed.
    /// </summary>
    [Then(@"^the user details contain no password or token$")]
    public void ThenTheUserDetailsContainNoCredentials()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());

        string[] forbidden = ["password", "hash", "accesstoken", "refreshtoken"];
        foreach (JsonProperty property in world.Response.Json.EnumerateObject())
        {
            string name = property.Name.ToLowerInvariant();
            Assert.False(forbidden.Any(name.Contains), $"The user details expose '{property.Name}': {world.Response.Body}");
        }

        Assert.DoesNotContain(NewUserPassword, world.Response.Body, StringComparison.Ordinal);
    }

    [Then(@"^the task ""([^""]+)"" shows the assignee name ""([^""]+)""$")]
    public async Task ThenTheTaskShowsTheAssigneeName(string title, string displayName)
    {
        Guid taskId = world.TaskNamed(title);
        Guid observer = await seeder.ObserverForOrganizationAsync(await seeder.OrganizationOfTaskAsync(taskId));
        ApiResponse task = await api.SendAsUserAsync(observer, HttpMethod.Get, $"/api/v1/tasks/{taskId}");

        Assert.True(task.IsSuccess, task.ToString());
        Assert.Equal(displayName, task.Json.GetProperty("assigneeDisplayName").GetString());
    }

    [Then(@"^(\w+) can still view the user (\w+)$")]
    public async Task ThenPersonaCanStillViewTheUser(string persona, string user)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/users/{world.PersonaNamed(user).UserId}");
        Assert.True(response.IsSuccess, response.ToString());
    }

    // ---- Helpers ----

    private static Dictionary<string, object?> Body(string displayName, string email, string password) => new()
    {
        ["displayName"] = displayName,
        ["email"] = email,
        ["password"] = password,
    };

    private async Task<ApiResponse> CreateAsync(string persona, Dictionary<string, object?> body)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/users", body);

        if (response.IsSuccess && body["displayName"] is string displayName)
        {
            var created = new World.Persona(
                displayName,
                response.Json.GetProperty("id").GetGuid(),
                response.Json.GetProperty("organizationId").GetGuid());

            world.Personas[displayName] = created;
            world.PersonaPasswords[displayName] = (string)body["password"]!;

            string firstName = displayName.Split(' ')[0];
            if (world.Personas.TryAdd(firstName, created with { Name = firstName }))
            {
                world.PersonaPasswords[firstName] = (string)body["password"]!;
            }
        }

        return response;
    }

    private async Task<ApiResponse> UpdateAsync(string persona, string user, object body, string suffix)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        string? ifMatch = await VersionSuffix.ResolveAsync(suffix, world, userId, () => seeder.CurrentUserVersionAsync(userId));
        return await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/users/{userId}", body, ifMatch);
    }

    private async Task<ApiResponse> ChangeOrgRoleAsync(string persona, string user, object newOrgRole)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        return await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/users/{userId}/org-role", new { newOrgRole },
            await seeder.CurrentUserVersionAsync(userId));
    }

    private async Task<ApiResponse> PostLifecycleAsync(string persona, string user, string operation)
    {
        Guid userId = world.PersonaNamed(user).UserId;
        return await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/users/{userId}/{operation}", body: null,
            await seeder.CurrentUserVersionAsync(userId));
    }

    /// <summary>Reqnroll reads the first line of a header-less table as its header, so it is included.</summary>
    private static Dictionary<string, string> TwoColumn(DataTable table) =>
        new[] { (Field: table.Header.ElementAt(0), Value: table.Header.ElementAt(1)) }
            .Concat(table.Rows.Select(r => (Field: r[0], Value: r[1])))
            .ToDictionary(e => e.Field.Trim(), e => e.Value.Trim(), StringComparer.Ordinal);

    private static int Length(string variant) =>
        int.Parse(variant.Split('-')[0].Split(' ')[^1], CultureInfo.InvariantCulture);
}
