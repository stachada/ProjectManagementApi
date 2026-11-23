using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Organizations;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>Organization administration — creation, updates, suspension — and read-backs of its state.</summary>
[Binding]
internal sealed class OrganizationSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    // ---- Arrangements ----

    [Given(@"^(\w+) has read the organization ""([^""]+)""$")]
    public async Task GivenPersonaHasReadTheOrganization(string persona, string organization)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/organizations/{organizationId}");
        Assert.True(response.IsSuccess && response.ETag is not null,
            $"Arrangement failed — {persona} could not read organization '{organization}': {response}");
        world.ReadVersions[(persona, organizationId)] = response.ETag;
    }

    /// <summary>A rename by a hidden second org Admin, so the organization's version moves on.</summary>
    [Given(@"^another \w+ admin has since renamed the organization ""([^""]+)"" to ""([^""]+)""$")]
    public async Task GivenAnotherAdminHasRenamedTheOrganization(string organization, string newName)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        Guid otherAdmin = await seeder.ObserverForOrganizationAsync(organizationId);
        string? description = (await seeder.FindOrganizationAsync(organizationId)).Description;

        ApiResponse response = await api.SendAsUserAsync(otherAdmin, HttpMethod.Put, $"/api/v1/organizations/{organizationId}",
            new { name = newName, description }, await seeder.CurrentOrganizationVersionAsync(organizationId));
        Assert.True(response.IsSuccess, $"Arrangement failed — could not rename organization '{organization}': {response}");
        world.Organizations[newName] = organizationId;
    }

    // ---- Creation ----

    [When("^" + Actor + @" creates an organization ""([^""]+)""$")]
    public Task WhenPersonaCreatesAnOrganization(string persona, string name) =>
        CreateAsync(persona, new Dictionary<string, object?> { ["name"] = name });

    [When("^" + Actor + @" creates an organization ""([^""]+)"" described as ""([^""]+)""$")]
    public Task WhenPersonaCreatesADescribedOrganization(string persona, string name, string description) =>
        CreateAsync(persona, new Dictionary<string, object?> { ["name"] = name, ["description"] = description });

    /// <summary>The body is a dictionary so that "without a name" can leave the field out entirely.</summary>
    [When("^" + Actor + @" creates an organization (without a name|with a \d+-character name|with a \d+-character description)$")]
    public Task WhenPersonaCreatesAnInvalidOrganization(string persona, string variant)
    {
        var body = new Dictionary<string, object?>();

        if (variant.EndsWith("character name", StringComparison.Ordinal))
        {
            body["name"] = new string('n', Length(variant));
        }
        else if (variant.EndsWith("character description", StringComparison.Ordinal))
        {
            body["name"] = "Initech";
            body["description"] = new string('d', Length(variant));
        }

        return CreateAsync(persona, body);
    }

    // ---- Viewing ----

    [When("^" + Actor + @" views an unknown organization$")]
    public Task WhenPersonaViewsAnUnknownOrganization(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/organizations/{Guid.CreateVersion7()}");

    [When(@"^(\w+) lists the projects of organization ""([^""]+)"" filtered by member (\w+)$")]
    public Task WhenListsOrganizationProjectsByMember(string persona, string organization, string member) =>
        ListProjectsAsync(persona, organization, $"memberId={world.PersonaNamed(member).UserId}");

    [When(@"^(\w+) lists the projects of organization ""([^""]+)"" including archived ones$")]
    public Task WhenListsOrganizationProjectsIncludingArchived(string persona, string organization) =>
        ListProjectsAsync(persona, organization, "includeArchived=true");

    [When(@"^(\w+) lists the projects of organization ""([^""]+)"" sorted by (\w+) (ascending|descending)$")]
    public Task WhenListsOrganizationProjectsSorted(string persona, string organization, string field, string direction) =>
        ListProjectsAsync(persona, organization, $"sortBy={field}&sortDescending={(direction == "descending" ? "true" : "false")}");

    // ---- Updates ----

    [When("^" + Actor + @" renames the organization ""([^""]+)"" to ""([^""]+)""" + VersionSuffix.Pattern + "$")]
    public async Task WhenPersonaRenamesTheOrganization(string persona, string organization, string newName, string suffix)
    {
        string? description = (await seeder.FindOrganizationAsync(world.OrganizationNamed(organization))).Description;
        await UpdateAsync(persona, organization, new { name = newName, description }, newName, suffix);
    }

    [When("^" + Actor + @" updates the organization ""([^""]+)"" to name ""([^""]+)"" and description ""([^""]+)""$")]
    public Task WhenPersonaUpdatesTheOrganization(string persona, string organization, string name, string description) =>
        UpdateAsync(persona, organization, new { name, description }, name, suffix: string.Empty);

    [When("^" + Actor + @" updates the organization ""([^""]+)"" with (a blank name|a \d+-character name|a \d+-character description)$")]
    public async Task WhenPersonaUpdatesTheOrganizationInvalidly(string persona, string organization, string variant)
    {
        Organization current = await seeder.FindOrganizationAsync(world.OrganizationNamed(organization));

        object body = variant switch
        {
            "a blank name" => new { name = "   ", description = current.Description },
            _ when variant.EndsWith("character name", StringComparison.Ordinal) =>
                new { name = new string('n', Length(variant)), description = current.Description },
            _ => new { name = current.Name, description = (string?)new string('d', Length(variant)) },
        };

        await UpdateAsync(persona, organization, body, newName: null, suffix: string.Empty);
    }

    // ---- Suspension ----

    [When("^" + Actor + @" (suspends|reactivates) the organization ""([^""]+)""$")]
    public async Task WhenPersonaSuspendsOrReactivatesTheOrganization(string persona, string action, string organization)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        string operation = action == "suspends" ? "suspend" : "reactivate";
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/organizations/{organizationId}/{operation}", body: null,
            await seeder.CurrentOrganizationVersionAsync(organizationId));
    }

    // ---- Assertions ----

    [Then(@"^the response points to the new organization$")]
    public void ThenTheResponsePointsToTheNewOrganization()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Guid id = world.Response.Json.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/organizations/{id}", world.Response.Location?.AbsolutePath);
    }

    [Then(@"^the organization ""([^""]+)"" has the slug ""([^""]+)""$")]
    public async Task ThenTheOrganizationHasTheSlug(string organization, string slug)
    {
        Assert.Equal(slug, (await seeder.FindOrganizationAsync(world.OrganizationNamed(organization))).Slug);
    }

    [Then(@"^the organization ""([^""]+)"" has description ""([^""]+)""$")]
    public async Task ThenTheOrganizationHasDescription(string organization, string description)
    {
        Assert.Equal(description, (await seeder.FindOrganizationAsync(world.OrganizationNamed(organization))).Description);
    }

    [Then(@"^the organization ""([^""]+)"" is (active|suspended)$")]
    public async Task ThenTheOrganizationIsActive(string organization, string state)
    {
        Assert.Equal(state == "active", (await seeder.FindOrganizationAsync(world.OrganizationNamed(organization))).IsActive);
    }

    [Then(@"^the organization details show name, slug, description, active flag, creation and update times$")]
    public void ThenTheOrganizationDetailsShowTheFields()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        JsonElement organization = world.Response.Json;

        foreach (string field in new[] { "name", "slug", "isActive", "createdAt", "updatedAt" })
        {
            Assert.True(organization.TryGetProperty(field, out JsonElement value) && value.ValueKind != JsonValueKind.Null,
                $"Expected a '{field}' in the organization details: {world.Response.Body}");
        }

        // Description is optional, so it may be null — but it is part of the representation.
        Assert.True(organization.TryGetProperty("description", out _),
            $"Expected a 'description' in the organization details: {world.Response.Body}");
    }

    // ---- Helpers ----

    private async Task CreateAsync(string persona, Dictionary<string, object?> body)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/organizations", body);

        if (response.IsSuccess && body.TryGetValue("name", out object? name) && name is string organizationName)
        {
            world.Organizations[organizationName] = response.Json.GetProperty("id").GetGuid();
        }
    }

    private Task ListProjectsAsync(string persona, string organization, string query) =>
        api.SendAsync(persona, HttpMethod.Get,
            $"/api/v1/organizations/{world.OrganizationNamed(organization)}/projects?pageSize=100&{query}");

    private async Task UpdateAsync(string persona, string organization, object body, string? newName, string suffix)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        string? ifMatch = await VersionSuffix.ResolveAsync(suffix, world, organizationId,
            () => seeder.CurrentOrganizationVersionAsync(organizationId));

        ApiResponse response = await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/organizations/{organizationId}", body, ifMatch);

        if (response.IsSuccess && newName is not null)
        {
            world.Organizations[newName] = organizationId;
        }
    }

    private static int Length(string variant) =>
        int.Parse(variant.Split('-')[0].Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture);
}
