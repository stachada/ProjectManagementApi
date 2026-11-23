using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Read-back assertions on projects: stored state (read from the database), the project
/// representation and its <c>_links</c>, project and board lists, and membership.
/// </summary>
[Binding]
internal sealed class ProjectDetailSteps(World world, Seeder seeder, ApiDriver api)
{
    // ---- Stored state ----

    [Then(@"^the project ""([^""]+)"" has the slug ""([^""]+)""$")]
    public async Task ThenTheProjectHasTheSlug(string project, string slug) =>
        Assert.Equal(slug, (await ProjectAsync(project)).Slug);

    [Then(@"^the project ""([^""]+)"" has no description$")]
    public async Task ThenTheProjectHasNoDescription(string project) =>
        Assert.Null((await ProjectAsync(project)).Description);

    [Then(@"^the project ""([^""]+)"" has description ""([^""]+)""$")]
    public async Task ThenTheProjectHasDescription(string project, string description) =>
        Assert.Equal(description, (await ProjectAsync(project)).Description);

    [Then(@"^the project ""([^""]+)"" exists$")]
    public async Task ThenTheProjectExists(string project) =>
        Assert.NotNull(await seeder.FindProjectAsync(world.ProjectNamed(project)));

    /// <summary>Deleted projects are hidden by the soft-delete query filter, exactly as the API sees them.</summary>
    [Then(@"^the project ""([^""]+)"" is not found$")]
    public async Task ThenTheProjectIsNotFound(string project) =>
        Assert.Null(await seeder.FindProjectAsync(world.ProjectNamed(project)));

    [Then(@"^the project ""([^""]+)"" (is|is not) archived$")]
    public async Task ThenTheProjectIsArchived(string project, string verb) =>
        Assert.Equal(verb == "is", (await ProjectAsync(project)).IsArchived);

    [Then(@"^the project ""([^""]+)"" is not listed for (\w+)$")]
    public async Task ThenTheProjectIsNotListedFor(string project, string persona)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, "/api/v1/projects?pageSize=100&includeArchived=true");
        Assert.True(response.IsSuccess, response.ToString());
        Assert.DoesNotContain(world.ProjectNamed(project), response.Json.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
    }

    /// <summary>Through the API, as the org-Admin observer — who can see every live board of the organization.</summary>
    [Then(@"^the board ""([^""]+)"" is not found$")]
    public async Task ThenTheBoardIsNotFound(string board)
    {
        Guid boardId = world.BoardNamed(board);
        ApiResponse response = await api.SendAsUserAsync(await seeder.ObserverForBoardAsync(boardId), HttpMethod.Get, $"/api/v1/boards/{boardId}");
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.NotFound, $"Expected 404 for board '{board}', got {response}");
    }

    [Then(@"^the task ""([^""]+)"" is not listed among all tasks for (\w+)$")]
    public async Task ThenTheTaskIsNotListedAmongAllTasks(string title, string persona)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, "/api/v1/tasks?pageSize=100");
        Assert.True(response.IsSuccess, response.ToString());
        Assert.DoesNotContain(world.TaskNamed(title), response.Json.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
    }

    [Then(@"^(\w+) can still view the task ""([^""]+)""$")]
    public async Task ThenPersonaCanStillViewTheTask(string persona, string title)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{world.TaskNamed(title)}");
        Assert.True(response.IsSuccess, response.ToString());
    }

    [Then(@"^(\w+) can no longer view the project ""([^""]+)""$")]
    public async Task ThenPersonaCanNoLongerViewTheProject(string persona, string project)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Membership ----

    [Then(@"^(\w+) (?:is|is still) an? (Viewer|Member|Admin) of project ""([^""]+)""$")]
    public async Task ThenPersonaIsAMember(string persona, Role role, string project)
    {
        Guid userId = world.PersonaNamed(persona).UserId;
        ProjectMember? member = (await seeder.ProjectMembersAsync(world.ProjectNamed(project))).SingleOrDefault(m => m.UserId == userId);
        Assert.True(member is not null, $"{persona} is not a member of project '{project}'.");
        Assert.Equal(role, member.Role);
    }

    [Then(@"^(\w+) is not a member of project ""([^""]+)""$")]
    public async Task ThenPersonaIsNotAMember(string persona, string project)
    {
        Guid userId = world.PersonaNamed(persona).UserId;
        Assert.DoesNotContain(await seeder.ProjectMembersAsync(world.ProjectNamed(project)), m => m.UserId == userId);
    }

    /// <summary>The project's members as stored, in any order. Columns: member, role.</summary>
    [Then(@"^the members of project ""([^""]+)"" are:$")]
    public async Task ThenTheMembersAre(string project, DataTable expected)
    {
        List<(string, Role)> actual = (await seeder.ProjectMembersAsync(world.ProjectNamed(project)))
            .Select(m => (PersonaName(m.UserId), m.Role))
            .OrderBy(m => m.Item1, StringComparer.Ordinal)
            .ToList();
        List<(string, Role)> wanted = expected.Rows
            .Select(r => (r["member"], Enum.Parse<Role>(r["role"])))
            .OrderBy(m => m.Item1, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(wanted, actual);
    }

    /// <summary>The members as the API listed them, in order. Columns: member, role.</summary>
    [Then(@"^the members of project ""([^""]+)"" are, in joining order:$")]
    public void ThenTheListedMembersAreInJoiningOrder(string project, DataTable expected)
    {
        _ = world.ProjectNamed(project);
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        List<(string, string)> actual = world.Response.Json.EnumerateArray()
            .Select(m => (m.GetProperty("displayName").GetString()!, m.GetProperty("role").GetString()!))
            .ToList();
        Assert.Equal(expected.Rows.Select(r => (r["member"], r["role"])).ToList(), actual);
    }

    [Then(@"^the response points to the members of project ""([^""]+)""$")]
    public void ThenTheResponsePointsToTheMembers(string project) =>
        Assert.Equal($"/api/v1/projects/{world.ProjectNamed(project)}/members", world.Response.Location?.AbsolutePath);

    [Then(@"^the response points to the new project$")]
    public void ThenTheResponsePointsToTheNewProject()
    {
        Guid id = SuccessfulJson().GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/projects/{id}", world.Response.Location?.AbsolutePath);
    }

    // ---- Representation ----

    [Then(@"^the project details show name, slug, description, archived flag, creation and update times$")]
    public void ThenTheProjectDetailsShowTheFields()
    {
        JsonElement project = SuccessfulJson();
        foreach (string field in new[] { "name", "slug", "description", "isArchived", "createdAt", "updatedAt" })
        {
            Assert.True(project.TryGetProperty(field, out _), $"Project details are missing '{field}'.");
        }
    }

    [Then(@"^the project details list the boards ""([^""]+)""$")]
    public void ThenTheProjectDetailsListTheBoards(string boards) =>
        Assert.Equal(Names(boards), SuccessfulJson().GetProperty("boards").EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList());

    [Then(@"^the project details list (\d+) boards$")]
    public void ThenTheProjectDetailsListNBoards(int count) =>
        Assert.Equal(count, SuccessfulJson().GetProperty("boards").GetArrayLength());

    /// <summary>"Alice, Bob and Victor" — every member is listed with the role stored for them.</summary>
    [Then(@"^the project details list the members (.+) with their roles$")]
    public async Task ThenTheProjectDetailsListTheMembers(string members)
    {
        JsonElement project = SuccessfulJson();
        Dictionary<Guid, Role> stored = (await seeder.ProjectMembersAsync(project.GetProperty("id").GetGuid()))
            .ToDictionary(m => m.UserId, m => m.Role);
        List<JsonElement> listed = project.GetProperty("members").EnumerateArray().ToList();

        Assert.Equal(
            Names(members.Replace(" and ", ", ", StringComparison.Ordinal)).Order(StringComparer.Ordinal),
            listed.Select(m => m.GetProperty("displayName").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(listed, m => Assert.Equal(stored[m.GetProperty("userId").GetGuid()].ToString(), m.GetProperty("role").GetString()));
    }

    [Then(@"^the project details state the current version$")]
    public async Task ThenTheProjectDetailsStateTheCurrentVersion()
    {
        JsonElement project = SuccessfulJson();
        string current = await seeder.CurrentProjectVersionAsync(project.GetProperty("id").GetGuid());
        Assert.Equal(current, world.Response.ETag);
        Assert.Equal(current, $"\"{project.GetProperty("concurrencyToken").GetString()}\"");
    }

    [Then(@"^the project has the links:$")]
    public void ThenTheProjectHasTheLinks(DataTable expected)
    {
        List<JsonElement> links = SuccessfulJson().GetProperty("_links").EnumerateArray().ToList();
        foreach (DataTableRow row in expected.Rows)
        {
            Assert.Contains(links, l => l.GetProperty("rel").GetString() == row["rel"] && l.GetProperty("method").GetString() == row["method"]);
        }
    }

    // ---- Lists ----

    [Then(@"^the listed projects are ""([^""]+)""$")]
    public void ThenTheListedProjectsAre(string projects) =>
        Assert.Equal(Names(projects), ListedNames());

    [Then(@"^no projects are listed$")]
    public void ThenNoProjectsAreListed() =>
        Assert.Empty(ListedNames());

    [Then(@"^the listed boards are ""([^""]+)""$")]
    public void ThenTheListedBoardsAre(string boards) =>
        Assert.Equal(Names(boards), ListedNames());

    [Then(@"^the boards of project ""([^""]+)"" are ""([^""]+)""$")]
    public async Task ThenTheBoardsOfTheProjectAre(string project, string boards)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid observer = await seeder.ObserverForOrganizationAsync(world.ProjectOrganizations[projectId]);
        ApiResponse response = await api.SendAsUserAsync(observer, HttpMethod.Get, $"/api/v1/projects/{projectId}/boards");
        Assert.True(response.IsSuccess, response.ToString());
        Assert.Equal(Names(boards), response.Json.EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList());
    }

    [Then(@"^the entry for ""([^""]+)"" shows its name, slug, organization and archived flag$")]
    public async Task ThenTheProjectEntryShowsItsSummary(string project)
    {
        Guid projectId = world.ProjectNamed(project);
        JsonElement entry = SuccessfulJson().EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == projectId);
        Project stored = await ProjectAsync(project);

        Assert.Equal(stored.Name, entry.GetProperty("name").GetString());
        Assert.Equal(stored.Slug, entry.GetProperty("slug").GetString());
        Assert.Equal(stored.OrganizationId, entry.GetProperty("organizationId").GetGuid());
        Assert.Equal(stored.IsArchived, entry.GetProperty("isArchived").GetBoolean());
    }

    // ---- Helpers ----

    private async Task<Project> ProjectAsync(string project) =>
        await seeder.FindProjectAsync(world.ProjectNamed(project))
            ?? throw new InvalidOperationException($"Project '{project}' does not exist.");

    private string PersonaName(Guid userId) =>
        world.Personas.Values.FirstOrDefault(p => p.UserId == userId)?.Name ?? $"<unknown user {userId}>";

    private List<string> ListedNames() =>
        SuccessfulJson().EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();

    private JsonElement SuccessfulJson()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        return world.Response.Json;
    }

    private static List<string> Names(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
