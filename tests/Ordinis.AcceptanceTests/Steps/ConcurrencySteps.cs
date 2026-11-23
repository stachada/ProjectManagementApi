using System.Net;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Tasks;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Optimistic concurrency end to end: the version (ETag) a read states, how a write states it
/// (If-Match, in any ETag notation), and what happens when two writers race.
/// </summary>
[Binding]
internal sealed class ConcurrencySteps(World world, Seeder seeder, ApiDriver api)
{
    // ---- Reading versions ----

    [Given(@"^(\w+) and (\w+) have both read the task ""([^""]+)""$")]
    public async Task GivenBothHaveReadTheTask(string first, string second, string title)
    {
        await ReadTaskAsync(first, title);
        await ReadTaskAsync(second, title);
    }

    [When(@"^(\w+) reads the task ""([^""]+)"" again$")]
    public Task WhenPersonaReadsTheTaskAgain(string persona, string title) =>
        ReadTaskAsync(persona, title);

    /// <summary>Someone else changes the task after the persona read it, so the persona's write is stale.</summary>
    [Given(@"^(\w+)'s change to the task ""([^""]+)"" was rejected as conflicting$")]
    public async Task GivenPersonasChangeWasRejected(string persona, string title)
    {
        Guid taskId = world.TaskNamed(title);
        string staleVersion = await ReadTaskAsync(persona, title);

        Guid someoneElse = await seeder.ObserverForBoardAsync((await seeder.FindTaskAsync(taskId))!.BoardId);
        ApiResponse change = await api.SendAsUserAsync(someoneElse, HttpMethod.Post, $"/api/v1/tasks/{taskId}/move",
            new { status = ProjectTaskStatus.ToDo }, await seeder.CurrentTaskVersionAsync(taskId));
        Assert.True(change.IsSuccess, $"Arrangement failed — the concurrent change was rejected: {change}");

        ApiResponse rejected = await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{taskId}/move",
            new { status = ProjectTaskStatus.Cancelled }, staleVersion);
        Assert.True(rejected.StatusCode == HttpStatusCode.Conflict, $"Arrangement failed — expected a conflict, got {rejected}");
    }

    // ---- Writing with a stated version ----

    [When(@"^(\w+) moves the task ""([^""]+)"" to (\w+) stating the version ""([^""]*)""$")]
    public Task WhenPersonaMovesStatingAVersion(string persona, string title, ProjectTaskStatus status, string version) =>
        api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{world.TaskNamed(title)}/move", new { status }, version);

    [When(@"^(\w+) moves the task ""([^""]+)"" to (\w+) stating the version read as (a quoted strong ETag|a weak ETag \(W/""…""\)|the bare token)$")]
    public Task WhenPersonaMovesStatingTheReadVersionAs(string persona, string title, ProjectTaskStatus status, string notation)
    {
        Guid taskId = world.TaskNamed(title);
        string quoted = world.ReadVersions[(persona, taskId)];
        string version = notation switch
        {
            "a quoted strong ETag" => quoted,
            "the bare token" => quoted.Trim('"'),
            _ => $"W/{quoted}",
        };

        return api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", new { status }, version);
    }

    [When(@"^(\w+) moves the task ""([^""]+)"" to (\w+) using an outdated version$")]
    public async Task WhenPersonaMovesUsingAnOutdatedVersion(string persona, string title, ProjectTaskStatus status)
    {
        Guid taskId = world.TaskNamed(title);
        string outdated = await seeder.CurrentTaskVersionAsync(taskId);

        Guid someoneElse = await seeder.ObserverForBoardAsync((await seeder.FindTaskAsync(taskId))!.BoardId);
        ApiResponse change = await api.SendAsUserAsync(someoneElse, HttpMethod.Post, $"/api/v1/tasks/{taskId}/move",
            new { status = ProjectTaskStatus.Cancelled }, outdated);
        Assert.True(change.IsSuccess, $"Arrangement failed — the concurrent change was rejected: {change}");

        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", new { status }, outdated);
    }

    /// <summary>Both writes are sent at the same time, each stating the version its writer read.</summary>
    [When(@"^(\w+) and (\w+) simultaneously move the task ""([^""]+)"" to (\w+) and (\w+) using the versions they read$")]
    public async Task WhenBothMoveSimultaneously(
        string first, string second, string title, ProjectTaskStatus firstStatus, ProjectTaskStatus secondStatus)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse[] responses = await Task.WhenAll(
            MoveAsUserAsync(first, taskId, firstStatus),
            MoveAsUserAsync(second, taskId, secondStatus));
        world.ConcurrentResponses.AddRange(responses);
    }

    // ---- Assertions ----

    /// <summary>
    /// <c>&lt;resource&gt;</c> is one of: the task "T" (optionally "in API v2"), the project "P",
    /// the board "B", the organization "O", the user U.
    /// </summary>
    [Then(@"^the response states the current version of (.+)$")]
    public async Task ThenTheResponseStatesTheCurrentVersion(string resource)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal(await CurrentVersionOfAsync(resource), world.Response.ETag);
    }

    [Then(@"^the stated version matches the version in the body$")]
    public void ThenTheStatedVersionMatchesTheBody()
    {
        Assert.Equal(world.Response.ETag, $"\"{world.Response.Json.GetProperty("concurrencyToken").GetString()}\"");
    }

    [Then(@"^the current version of the (task|project) ""([^""]+)"" (differs from|equals) the one (\w+) read$")]
    public async Task ThenTheCurrentVersionComparesToTheRead(string kind, string name, string comparison, string reader)
    {
        Guid id = kind == "task" ? world.TaskNamed(name) : world.ProjectNamed(name);
        string current = kind == "task" ? await seeder.CurrentTaskVersionAsync(id) : await seeder.CurrentProjectVersionAsync(id);
        string read = world.ReadVersions[(reader, id)];

        if (comparison == "equals")
        {
            Assert.Equal(read, current);
        }
        else
        {
            Assert.NotEqual(read, current);
        }
    }

    [Then(@"^the error explains that the task was changed concurrently$")]
    public void ThenTheErrorExplainsTheConcurrentChange()
    {
        JsonElement problem = world.Response.Json;
        Assert.Equal("Concurrency conflict", problem.GetProperty("title").GetString());
        Assert.Contains("concurrency conflict", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"^the error says a version \(If-Match\) is required$")]
    public void ThenTheErrorSaysAVersionIsRequired()
    {
        Assert.True(world.Response.StatusCode == HttpStatusCode.UnprocessableEntity, world.Response.ToString());
        JsonElement errors = world.Response.Json.GetProperty("errors");
        JsonProperty ifMatch = errors.EnumerateObject().SingleOrDefault(e => e.Name.Equals("IfMatch", StringComparison.OrdinalIgnoreCase));

        Assert.True(ifMatch.Value.ValueKind == JsonValueKind.Array, $"Expected an error on the version (If-Match), got {world.Response}");
        Assert.Contains("If-Match", ifMatch.Value[0].GetString(), StringComparison.Ordinal);
    }

    [Then(@"^exactly one request succeeds$")]
    public void ThenExactlyOneRequestSucceeds()
    {
        Assert.True(world.ConcurrentResponses.Count(r => r.IsSuccess) == 1,
            $"Expected exactly one success: {string.Join(" | ", world.ConcurrentResponses)}");
    }

    [Then(@"^the other is rejected as conflicting$")]
    public void ThenTheOtherIsRejectedAsConflicting()
    {
        ApiResponse other = world.ConcurrentResponses.Single(r => !r.IsSuccess);
        Assert.True(other.StatusCode == HttpStatusCode.Conflict, $"Expected a conflict, got {other}");
    }

    // ---- Helpers ----

    private async Task<string> ReadTaskAsync(string persona, string title)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        Assert.True(response.IsSuccess && response.ETag is not null, $"{persona} could not read the task '{title}': {response}");
        world.ReadVersions[(persona, taskId)] = response.ETag;
        return response.ETag;
    }

    private Task<ApiResponse> MoveAsUserAsync(string persona, Guid taskId, ProjectTaskStatus status) =>
        api.SendAsUserAsync(world.PersonaNamed(persona).UserId, HttpMethod.Post, $"/api/v1/tasks/{taskId}/move",
            new { status }, world.ReadVersions[(persona, taskId)]);

    private async Task<string> CurrentVersionOfAsync(string resource)
    {
        string name = resource.Contains('"') ? resource.Split('"')[1] : resource.Split(' ')[^1];

        return resource.Split(' ')[1] switch
        {
            "task" => await seeder.CurrentTaskVersionAsync(world.TaskNamed(name)),
            "project" => await seeder.CurrentProjectVersionAsync(world.ProjectNamed(name)),
            "board" => await seeder.CurrentBoardVersionAsync(world.BoardNamed(name)),
            "organization" => await seeder.CurrentOrganizationVersionAsync(world.OrganizationNamed(name)),
            "user" => await seeder.CurrentUserVersionAsync(world.PersonaNamed(name).UserId),
            _ => throw new InvalidOperationException($"Unknown versioned resource '{resource}'."),
        };
    }
}
