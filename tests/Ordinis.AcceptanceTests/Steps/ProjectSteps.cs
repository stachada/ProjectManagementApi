using Ordinis.AcceptanceTests.Support;
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
