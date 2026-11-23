using System.Net;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Steps of the authorization features: actions named by role ("the Viewer moves …"), reads of
/// resources that may be hidden, and checks that a response reveals nothing it should not.
/// </summary>
[Binding]
internal sealed class AuthorizationSteps(World world, Seeder seeder, ApiDriver api, ITestRunner runner)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>
    /// The README's persona convention: each role in the permission matrix is played by one
    /// persona of the feature's Background.
    /// </summary>
    private static readonly Dictionary<string, string> PersonaOfRole = new(StringComparer.Ordinal)
    {
        ["Viewer"] = "Victor",
        ["Member"] = "Bob",
        ["Admin"] = "Alice",
        ["Org Admin"] = "Olivia",
    };

    /// <summary>
    /// "the Viewer &lt;action&gt;" runs "Victor &lt;action&gt;" — so the matrix rows reuse the
    /// persona steps instead of duplicating every action for each role.
    /// </summary>
    [When(@"^the (Viewer|Member|Admin|Org Admin) (.+)$")]
    public Task WhenTheRolePerformsAnAction(string role, string action) =>
        runner.WhenAsync($"{PersonaOfRole[role]} {action}", null, null, "When ");

    // ---- Reads that may be hidden ----

    [When("^" + Actor + @" views an unknown project$")]
    public Task WhenPersonaViewsAnUnknownProject(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{Guid.CreateVersion7()}");

    [When("^" + Actor + @" views the audit trail of project ""([^""]+)""$")]
    public Task WhenPersonaViewsTheAuditTrail(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}/audit");

    [When("^" + Actor + @" views the audit trail of an unknown project$")]
    public Task WhenPersonaViewsTheAuditTrailOfAnUnknownProject(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{Guid.CreateVersion7()}/audit");

    [When("^" + Actor + @" views the comments of an unknown task$")]
    public Task WhenPersonaViewsTheCommentsOfAnUnknownTask(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{Guid.CreateVersion7()}/comments");

    [When("^" + Actor + @" lists the webhooks of project ""([^""]+)""$")]
    public Task WhenPersonaListsTheWebhooks(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}/webhooks");

    // ---- Assertions ----

    [Then(@"^both requests are rejected as not found$")]
    public void ThenBothRequestsAreRejectedAsNotFound()
    {
        ApiResponse first = world.PreviousResponse ?? throw new InvalidOperationException("Only one response was recorded.");
        Assert.True(first.StatusCode == HttpStatusCode.NotFound, $"Expected the first request to be not found, got {first}");
        Assert.True(world.Response.StatusCode == HttpStatusCode.NotFound, $"Expected the second request to be not found, got {world.Response}");
    }

    [Then(@"^nothing from project ""([^""]+)"" is listed$")]
    public async Task ThenNothingFromTheProjectIsListed(string project) =>
        await AssertNothingListedFromAsync([world.ProjectNamed(project)]);

    [Then(@"^nothing from organization ""([^""]+)"" is listed$")]
    public async Task ThenNothingFromTheOrganizationIsListed(string organization)
    {
        Guid organizationId = world.OrganizationNamed(organization);
        await AssertNothingListedFromAsync(
            world.ProjectOrganizations.Where(p => p.Value == organizationId).Select(p => p.Key).ToList());
    }

    [Then(@"^the board ""([^""]+)"" has no task ""([^""]+)""$")]
    public async Task ThenTheBoardHasNoTask(string board, string title)
    {
        Guid boardId = world.BoardNamed(board);
        ApiResponse tasks = await api.SendAsUserAsync(await seeder.ObserverForBoardAsync(boardId), HttpMethod.Get,
            $"/api/v1/boards/{boardId}/tasks?pageSize=100");

        Assert.True(tasks.IsSuccess, tasks.ToString());
        Assert.DoesNotContain(title, tasks.Json.EnumerateArray().Select(t => t.GetProperty("title").GetString()));
    }

    /// <summary>
    /// The list succeeded, and no entry is — or refers to — one of the projects, their boards or
    /// their tasks: none of their IDs appears anywhere in the body.
    /// </summary>
    private async Task AssertNothingListedFromAsync(IReadOnlyCollection<Guid> projectIds)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal(JsonValueKind.Array, world.Response.Json.ValueKind);
        Assert.NotEmpty(projectIds);

        IEnumerable<Guid> hidden = projectIds.Concat(await seeder.BoardAndTaskIdsOfProjectsAsync(projectIds));
        foreach (Guid id in hidden)
        {
            Assert.DoesNotContain(id.ToString(), world.Response.Body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
