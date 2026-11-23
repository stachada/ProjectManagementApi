using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Reading a project's audit trail. "Reads" steps call the API as the named persona;
/// "the newest audit entry …" read back as a hidden org-Admin observer.
/// </summary>
[Binding]
internal sealed class AuditSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>One <c>TaskCreated</c> event per seeded task, on the project's first board.</summary>
    [Given(@"^(\d+) task events have occurred in project ""([^""]+)""$")]
    public async Task GivenTaskEventsHaveOccurred(int count, string project)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid boardId = world.BoardProjects.First(b => b.Value == projectId).Key;
        for (int i = 1; i <= count; i++)
        {
            await seeder.CreateTaskAsync(boardId, $"Audited task {i:00}");
        }
    }

    [When("^" + Actor + @" reads the audit trail of project ""([^""]+)""$")]
    public Task WhenPersonaReadsTheAuditTrail(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}/audit?pageSize=100");

    [When("^" + Actor + @" reads page (\d+) of the audit trail of project ""([^""]+)"" with (\d+) entries per page$")]
    public Task WhenPersonaReadsAPageOfTheAuditTrail(string persona, int page, string project, int pageSize) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/projects/{world.ProjectNamed(project)}/audit?page={page}&pageSize={pageSize}");

    [Then(@"^the newest audit entry of project ""([^""]+)"" is ""(\w+)"" by (\w+) for ""([^""]+)""$")]
    public async Task ThenTheNewestAuditEntryIs(string project, string eventType, string actor, string title)
    {
        JsonElement newest = (await AuditTrailAsync(project)).FirstOrDefault();
        Assert.True(newest.ValueKind == JsonValueKind.Object, $"The audit trail of '{project}' is empty.");
        Assert.Equal(eventType, newest.GetProperty("eventType").GetString());
        Assert.Equal(world.PersonaNamed(actor).UserId, newest.GetProperty("actorId").GetGuid());
        Assert.Equal(world.TaskNamed(title), TaskIdOf(newest));
    }

    [Then(@"^the entry records when it occurred and its event details$")]
    public void ThenTheEntryRecordsWhenAndWhat()
    {
        // The preceding step read the trail as the observer; the newest entry is the one it checked.
        JsonElement newest = Assert.Single(world.Response.Json.EnumerateArray().Take(1));
        DateTimeOffset occurredAt = newest.GetProperty("occurredAt").GetDateTimeOffset();
        Assert.InRange(occurredAt, TestHost.Clock.GetUtcNow().AddMinutes(-10), TestHost.Clock.GetUtcNow().AddMinutes(10));
        Assert.Equal(JsonValueKind.Object, newest.GetProperty("payload").ValueKind);
        Assert.NotEmpty(newest.GetProperty("payload").EnumerateObject());
    }

    /// <summary>Columns: event, actor — the whole page, in order.</summary>
    [Then(@"^the audit entries are, newest first:$")]
    public void ThenTheAuditEntriesAreNewestFirst(DataTable expected)
    {
        List<(string?, Guid)> actual = Entries()
            .Select(e => (e.GetProperty("eventType").GetString(), e.GetProperty("actorId").GetGuid()))
            .ToList();
        List<(string?, Guid)> wanted = expected.Rows
            .Select(r => ((string?)r["event"], world.PersonaNamed(r["actor"]).UserId))
            .ToList();
        Assert.Equal(wanted, actual);
    }

    [Then(@"^the audit entries include ""(\w+)"" and ""(\w+)"" for ""([^""]+)""$")]
    public void ThenTheAuditEntriesInclude(string firstEvent, string secondEvent, string title)
    {
        Guid taskId = world.TaskNamed(title);
        List<string?> events = Entries().Where(e => TaskIdOf(e) == taskId).Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains(firstEvent, events);
        Assert.Contains(secondEvent, events);
    }

    [Then(@"^the audit entries only concern ""([^""]+)""$")]
    public void ThenTheAuditEntriesOnlyConcern(string title)
    {
        List<JsonElement> entries = Entries();
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(world.TaskNamed(title), TaskIdOf(e)));
    }

    [Then(@"^the audit trail of project ""([^""]+)"" is empty$")]
    public async Task ThenTheAuditTrailIsEmpty(string project) =>
        Assert.Empty(await AuditTrailAsync(project));

    [Then(@"^(\d+) audit entries are returned$")]
    public void ThenNAuditEntriesAreReturned(int count) =>
        Assert.Equal(count, Entries().Count);

    // ---- Helpers ----

    private async Task<List<JsonElement>> AuditTrailAsync(string project)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid observer = await seeder.ObserverForOrganizationAsync(world.ProjectOrganizations[projectId]);
        ApiResponse response = await api.SendAsUserAsync(observer, HttpMethod.Get, $"/api/v1/projects/{projectId}/audit?pageSize=100");
        Assert.True(response.IsSuccess, $"Could not read the audit trail: {response}");
        world.LastResponse = response;
        return response.Json.EnumerateArray().ToList();
    }

    private List<JsonElement> Entries()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        return world.Response.Json.EnumerateArray().ToList();
    }

    private static Guid? TaskIdOf(JsonElement entry) =>
        entry.GetProperty("payload").TryGetProperty("TaskId", out JsonElement id) ? id.GetGuid() : null;
}
