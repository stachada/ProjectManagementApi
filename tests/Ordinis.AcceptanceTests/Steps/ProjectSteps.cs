using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Users;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Project actions. Like task writes, versioned project writes state the project's current
/// version unless the step says otherwise.
/// </summary>
[Binding]
internal sealed class ProjectSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    [Given(@"^(\w+) has deleted the project ""([^""]+)""$")]
    public async Task GivenPersonaHasDeletedTheProject(string persona, string project)
    {
        Guid projectId = world.ProjectNamed(project);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/projects/{projectId}",
            body: null, await seeder.CurrentProjectVersionAsync(projectId));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not delete project '{project}': {response}");
    }

    [When("^" + Actor + @" adds (\w+) to project ""([^""]+)"" as an? (Viewer|Member|Admin)$")]
    public async Task WhenPersonaAddsAMember(string persona, string member, string project, Role role)
    {
        Guid projectId = world.ProjectNamed(project);
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/projects/{projectId}/members",
            new { userId = world.PersonaNamed(member).UserId, role },
            await seeder.CurrentProjectVersionAsync(projectId));
    }

    [Given(@"^(\w+) has added (\w+) to project ""([^""]+)"" as an? (Viewer|Member|Admin)$")]
    public async Task GivenPersonaHasAddedAMember(string persona, string member, string project, Role role)
    {
        await WhenPersonaAddsAMember(persona, member, project, role);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not add {member}: {world.Response}");
    }

    [When("^" + Actor + @" renames the project ""([^""]+)"" to ""([^""]+)""$")]
    public async Task WhenPersonaRenamesTheProject(string persona, string project, string newName)
    {
        Guid projectId = world.ProjectNamed(project);
        string? description = (await seeder.FindProjectAsync(projectId))?.Description;

        ApiResponse response = await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/projects/{projectId}",
            new { newName, newDescription = description },
            await seeder.CurrentProjectVersionAsync(projectId));

        if (response.IsSuccess)
        {
            world.Projects[newName] = projectId;
        }
    }

    [When("^" + Actor + @" changes the role of (\w+) in project ""([^""]+)"" to (Viewer|Member|Admin)$")]
    public async Task WhenPersonaChangesAMembersRole(string persona, string member, string project, Role newRole)
    {
        Guid projectId = world.ProjectNamed(project);
        await api.SendAsync(persona, HttpMethod.Put,
            $"/api/v1/projects/{projectId}/members/{world.PersonaNamed(member).UserId}/role",
            new { newRole },
            await seeder.CurrentProjectVersionAsync(projectId));
    }

    [When("^" + Actor + @" removes (\w+) from project ""([^""]+)""$")]
    public async Task WhenPersonaRemovesAMember(string persona, string member, string project)
    {
        Guid projectId = world.ProjectNamed(project);
        await api.SendAsync(persona, HttpMethod.Delete,
            $"/api/v1/projects/{projectId}/members/{world.PersonaNamed(member).UserId}",
            body: null,
            await seeder.CurrentProjectVersionAsync(projectId));
    }

    [When("^" + Actor + @" creates a project ""([^""]+)"" in ""([^""]+)""$")]
    public Task WhenPersonaCreatesAProject(string persona, string project, string organization) =>
        CreateProjectAsync(persona, project, organization, description: null);

    [When("^" + Actor + @" creates a project ""([^""]+)"" in ""([^""]+)"" described as ""([^""]+)""$")]
    public Task WhenPersonaCreatesADescribedProject(string persona, string project, string organization, string description) =>
        CreateProjectAsync(persona, project, organization, description);

    private async Task CreateProjectAsync(string persona, string project, string organization, string? description)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/projects",
            new { organizationId, name = project, description });

        if (response.IsSuccess)
        {
            Guid projectId = response.Json.GetProperty("id").GetGuid();
            world.Projects[project] = projectId;
            world.ProjectOrganizations[projectId] = organizationId;
        }
    }
}
