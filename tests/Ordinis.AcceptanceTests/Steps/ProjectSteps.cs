using System.Globalization;
using System.Text.RegularExpressions;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Users;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Project actions: creation, update, archiving, deletion and membership. Versioned writes
/// state the project's current version unless the step ends with a <see cref="VersionSuffix"/>.
/// </summary>
[Binding]
internal sealed partial class ProjectSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";
    private const string Version = VersionSuffix.Pattern;

    /// <summary>An enum value the API does not define.</summary>
    private const int UnknownEnumValue = 99;

    // ---- Arrangement through the API ----

    [Given(@"^(\w+) has read the project ""([^""]+)""$")]
    public async Task GivenPersonaHasReadTheProject(string persona, string project)
    {
        Guid projectId = world.ProjectNamed(project);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{projectId}");
        Assert.True(response.IsSuccess && response.ETag is not null, $"Arrangement failed — {persona} could not read project '{project}': {response}");
        world.ReadVersions[(persona, projectId)] = response.ETag;
    }

    [Given(@"^(\w+) has since renamed the project ""([^""]+)"" to ""([^""]+)""$")]
    public async Task GivenPersonaHasRenamedTheProject(string persona, string project, string newName)
    {
        await WhenPersonaRenamesTheProject(persona, project, newName, suffix: string.Empty);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not rename project '{project}': {world.Response}");
    }

    [Given(@"^(\w+) has deleted the project ""([^""]+)""$")]
    public async Task GivenPersonaHasDeletedTheProject(string persona, string project)
    {
        await WhenPersonaDeletesTheProject(persona, project, suffix: string.Empty);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not delete project '{project}': {world.Response}");
    }

    [Given(@"^(\w+) has added (\w+) to project ""([^""]+)"" as an? (Viewer|Member|Admin)$")]
    public async Task GivenPersonaHasAddedAMember(string persona, string member, string project, Role role)
    {
        await WhenPersonaAddsAMember(persona, member, project, role, suffix: string.Empty);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not add {member}: {world.Response}");
    }

    [Given(@"^(\w+) has changed the role of (\w+) in project ""([^""]+)"" to (Viewer|Member|Admin)$")]
    public async Task GivenPersonaHasChangedAMembersRole(string persona, string member, string project, Role newRole)
    {
        await WhenPersonaChangesAMembersRole(persona, member, project, newRole, suffix: string.Empty);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not change the role of {member}: {world.Response}");
    }

    [Given(@"^(\w+) has removed (\w+) from project ""([^""]+)""$")]
    public async Task GivenPersonaHasRemovedAMember(string persona, string member, string project)
    {
        await WhenPersonaRemovesAMember(persona, member, project, suffix: string.Empty);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not remove {member}: {world.Response}");
    }

    // ---- Creation ----

    [When("^" + Actor + @" creates a project ""([^""]+)"" in ""([^""]+)""$")]
    public Task WhenPersonaCreatesAProject(string persona, string project, string organization) =>
        CreateProjectAsync(persona, project, world.OrganizationNamed(organization), new { name = project });

    [When("^" + Actor + @" creates a project ""([^""]+)"" in ""([^""]+)"" described as ""([^""]+)""$")]
    public Task WhenPersonaCreatesADescribedProject(string persona, string project, string organization, string description) =>
        CreateProjectAsync(persona, project, world.OrganizationNamed(organization), new { name = project, description });

    [When("^" + Actor + @" creates a project ""([^""]+)"" in an unknown organization$")]
    public Task WhenPersonaCreatesAProjectInAnUnknownOrganization(string persona, string project) =>
        CreateProjectAsync(persona, project, Guid.CreateVersion7(), new { name = project });

    [When("^" + Actor + @" creates a project in ""([^""]+)"" (without a name|with a \d+-character name|with a \d+-character description)$")]
    public Task WhenPersonaCreatesAProjectWithInvalidDetails(string persona, string organization, string variant)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        object details = variant switch
        {
            "without a name" => new { description = "No name given" },
            _ when variant.EndsWith("name", StringComparison.Ordinal) => new { name = new string('n', Length(variant)) },
            _ => new { name = "Described project", description = new string('d', Length(variant)) },
        };

        return CreateProjectAsync(persona, project: null, organizationId, details);
    }

    // ---- Viewing and listing ----

    [When("^" + Actor + @" views the project ""([^""]+)""$")]
    public Task WhenPersonaViewsTheProject(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}");

    [When("^" + Actor + @" lists the boards of project ""([^""]+)""$")]
    public Task WhenPersonaListsTheBoards(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}/boards");

    [When("^" + Actor + @" lists the members of project ""([^""]+)""$")]
    public Task WhenPersonaListsTheMembers(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}/members");

    [When(@"^(\w+) lists the projects filtered by member (\w+)$")]
    public Task WhenPersonaListsProjectsByMember(string persona, string member) =>
        ListProjectsAsync(persona, $"memberId={world.PersonaNamed(member).UserId}");

    [When(@"^(\w+) lists the projects filtered by organization ""([^""]+)""$")]
    public Task WhenPersonaListsProjectsByOrganization(string persona, string organization) =>
        ListProjectsAsync(persona, $"organizationId={world.OrganizationNamed(organization)}");

    [When(@"^(\w+) lists the projects including archived ones$")]
    public Task WhenPersonaListsProjectsIncludingArchived(string persona) =>
        ListProjectsAsync(persona, "includeArchived=true");

    [When(@"^(\w+) lists the projects sorted by (\w+) (ascending|descending)$")]
    public Task WhenPersonaListsProjectsSorted(string persona, string field, string direction) =>
        ListProjectsAsync(persona, $"sortBy={field}&sortDescending={(direction == "descending" ? "true" : "false")}");

    // ---- Update ----

    [When("^" + Actor + @" updates the project ""([^""]+)"" to name ""([^""]+)"" and description ""([^""]+)""$")]
    public Task WhenPersonaUpdatesTheProject(string persona, string project, string newName, string newDescription) =>
        UpdateAsync(persona, project, new { newName, newDescription }, newName, suffix: string.Empty);

    [When("^" + Actor + @" updates the project ""([^""]+)"" with (a blank name|a \d+-character name|a \d+-character description)$")]
    public async Task WhenPersonaUpdatesTheProjectWithInvalidDetails(string persona, string project, string variant)
    {
        Guid projectId = world.ProjectNamed(project);
        string? description = (await seeder.FindProjectAsync(projectId))?.Description;
        object body = variant switch
        {
            "a blank name" => new { newName = "   ", newDescription = description },
            _ when variant.EndsWith("name", StringComparison.Ordinal) => new { newName = new string('n', Length(variant)), newDescription = description },
            _ => new { newName = project, newDescription = (string?)new string('d', Length(variant)) },
        };

        await UpdateAsync(persona, project, body, newName: null, suffix: string.Empty);
    }

    [When("^" + Actor + @" renames the project ""([^""]+)"" to ""([^""]+)""" + Version + "$")]
    public async Task WhenPersonaRenamesTheProject(string persona, string project, string newName, string suffix)
    {
        string? description = (await seeder.FindProjectAsync(world.ProjectNamed(project)))?.Description;
        await UpdateAsync(persona, project, new { newName, newDescription = description }, newName, suffix);
    }

    // ---- Lifecycle ----

    [When("^" + Actor + @" archives the project ""([^""]+)""" + Version + "$")]
    public Task WhenPersonaArchivesTheProject(string persona, string project, string suffix) =>
        WriteAsync(persona, HttpMethod.Post, project, "/archive", body: null, suffix);

    [When("^" + Actor + @" unarchives the project ""([^""]+)""" + Version + "$")]
    public Task WhenPersonaUnarchivesTheProject(string persona, string project, string suffix) =>
        WriteAsync(persona, HttpMethod.Post, project, "/unarchive", body: null, suffix);

    [When("^" + Actor + @" deletes the project ""([^""]+)""" + Version + "$")]
    public Task WhenPersonaDeletesTheProject(string persona, string project, string suffix) =>
        WriteAsync(persona, HttpMethod.Delete, project, string.Empty, body: null, suffix);

    // ---- Membership ----

    [When("^" + Actor + @" adds (\w+) to project ""([^""]+)"" as an? (Viewer|Member|Admin)" + Version + "$")]
    public Task WhenPersonaAddsAMember(string persona, string member, string project, Role role, string suffix) =>
        WriteAsync(persona, HttpMethod.Post, project, "/members", new { userId = world.PersonaNamed(member).UserId, role }, suffix);

    [When("^" + Actor + @" adds (\w+) to project ""([^""]+)"" with an unknown role$")]
    public Task WhenPersonaAddsAMemberWithAnUnknownRole(string persona, string member, string project) =>
        WriteAsync(persona, HttpMethod.Post, project, "/members", new { userId = world.PersonaNamed(member).UserId, role = UnknownEnumValue }, string.Empty);

    [When("^" + Actor + @" adds an unknown user to project ""([^""]+)"" as an? (Viewer|Member|Admin)$")]
    public Task WhenPersonaAddsAnUnknownUser(string persona, string project, Role role) =>
        WriteAsync(persona, HttpMethod.Post, project, "/members", new { userId = Guid.CreateVersion7(), role }, string.Empty);

    [When("^" + Actor + @" changes the role of (\w+) in project ""([^""]+)"" to (Viewer|Member|Admin)" + Version + "$")]
    public Task WhenPersonaChangesAMembersRole(string persona, string member, string project, Role newRole, string suffix) =>
        WriteAsync(persona, HttpMethod.Put, project, $"/members/{world.PersonaNamed(member).UserId}/role", new { newRole }, suffix);

    [When("^" + Actor + @" changes the role of (\w+) in project ""([^""]+)"" to an unknown role$")]
    public Task WhenPersonaChangesAMembersRoleToAnUnknownRole(string persona, string member, string project) =>
        WriteAsync(persona, HttpMethod.Put, project, $"/members/{world.PersonaNamed(member).UserId}/role", new { newRole = UnknownEnumValue }, string.Empty);

    [When("^" + Actor + @" removes (\w+) from project ""([^""]+)""" + Version + "$")]
    public Task WhenPersonaRemovesAMember(string persona, string member, string project, string suffix) =>
        WriteAsync(persona, HttpMethod.Delete, project, $"/members/{world.PersonaNamed(member).UserId}", body: null, suffix);

    // ---- Helpers ----

    private async Task CreateProjectAsync(string persona, string? project, Guid organizationId, object details)
    {
        // Merge the organization into the anonymous details object via a dictionary, so a
        // variant can leave a field out entirely (absent ≠ null for model binding).
        Dictionary<string, object?> body = details.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(details), StringComparer.Ordinal);
        body["organizationId"] = organizationId;

        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/projects", body);

        if (response.IsSuccess && project is not null)
        {
            Guid projectId = response.Json.GetProperty("id").GetGuid();
            world.Projects[project] = projectId;
            world.ProjectOrganizations[projectId] = organizationId;
        }
    }

    private Task ListProjectsAsync(string persona, string query) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects?pageSize=100&{query}");

    private async Task UpdateAsync(string persona, string project, object body, string? newName, string suffix)
    {
        Guid projectId = world.ProjectNamed(project);
        await WriteAsync(persona, HttpMethod.Put, project, string.Empty, body, suffix);

        if (world.Response.IsSuccess && newName is not null)
        {
            world.Projects[newName] = projectId;
        }
    }

    private async Task WriteAsync(string persona, HttpMethod method, string project, string subPath, object? body, string suffix)
    {
        Guid projectId = world.ProjectNamed(project);
        string? ifMatch = await VersionSuffix.ResolveAsync(suffix, world, projectId, () => seeder.CurrentProjectVersionAsync(projectId));
        await api.SendAsync(persona, method, $"/api/v1/projects/{projectId}{subPath}", body, ifMatch);
    }

    private static int Length(string variant) =>
        int.Parse(DigitsPattern().Match(variant).Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsPattern();
}
