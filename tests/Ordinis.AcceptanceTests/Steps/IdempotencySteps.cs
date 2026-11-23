using System.Net;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Idempotent creation: a create sent with a request key (<c>Idempotency-Key</c>) and then
/// retried. The creates themselves are the ordinary action steps, run with the key through
/// <see cref="World.NextRequest"/>.
/// </summary>
[Binding]
internal sealed class IdempotencySteps(World world, Seeder seeder, ApiDriver api, ITestRunner runner)
{
    // ---- Sending a create, first and again ----

    /// <summary>"Bob has sent "creates a task …" with request key "k-1"" — the first attempt, which must succeed.</summary>
    [Given(@"^(\w+) has sent ""(.+)"" with request key ""([^""]+)""$")]
    public async Task GivenPersonaHasSent(string persona, string action, string key)
    {
        await CaptureCountsAsync();
        await RunWithKeyAsync($"{persona} {action}", key);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — the first attempt was rejected: {world.Response}");
    }

    [When(@"^(\w+) sends ""(.+)"" with request key ""([^""]+)"" again$")]
    public Task WhenPersonaSendsAgain(string persona, string action, string key) =>
        RunWithKeyAsync($"{persona} {action}", key);

    /// <remarks>
    /// The key suffix is one group whose last alternative is empty, so it always participates:
    /// Reqnroll drops unmatched groups, which would shift the parameters.
    /// </remarks>
    [Given(@"^(\w+) has created a task ""([^""]+)"" on board ""([^""]+)""( with request key ""[^""]+""|)$")]
    public async Task GivenPersonaHasCreatedATask(string persona, string title, string board, string keySuffix)
    {
        string? key = keySuffix.Length == 0 ? null : keySuffix.Split('"')[1];
        await RunWithKeyAsync($@"{persona} creates a task ""{title}"" on board ""{board}""", key);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not create the task: {world.Response}");
    }

    [Given(@"^(\w+)'s attempt to create a task ""([^""]+)"" on board ""([^""]+)"" with request key ""([^""]+)"" was rejected as invalid$")]
    public async Task GivenPersonasAttemptWasRejected(string persona, string title, string board, string key)
    {
        await RunWithKeyAsync($@"{persona} creates a task ""{title}"" on board ""{board}""", key);
        Assert.True(world.Response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"Arrangement failed — expected the attempt to be rejected as invalid: {world.Response}");
    }

    [Given(@"^(\w+) has moved the task ""([^""]+)"" to (\w+) with request key ""([^""]+)""$")]
    public async Task GivenPersonaHasMovedWithAKey(string persona, string title, string status, string key)
    {
        await RunWithKeyAsync($@"{persona} moves the task ""{title}"" to {status}", key);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not move the task: {world.Response}");
    }

    [Given(@"^(\w+) has unarchived the board ""([^""]+)""$")]
    public async Task GivenPersonaHasUnarchivedTheBoard(string persona, string board)
    {
        await runner.WhenAsync($@"{persona} unarchives the board ""{board}""", null, null, "When ");
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not unarchive the board: {world.Response}");
    }

    /// <summary>Two identical creates sent at the same moment, both with the same key.</summary>
    [When(@"^(\w+) sends two identical requests to create a task ""([^""]+)"" on board ""([^""]+)"" with request key ""([^""]+)"" at the same time$")]
    public async Task WhenPersonaSendsTwoIdenticalRequests(string persona, string title, string board, string key)
    {
        Guid userId = world.PersonaNamed(persona).UserId;
        object body = new { boardId = world.BoardNamed(board), title };

        ApiResponse[] responses = await Task.WhenAll(
            api.SendAsUserAsync(userId, HttpMethod.Post, "/api/v1/tasks", body, idempotencyKey: key),
            api.SendAsUserAsync(userId, HttpMethod.Post, "/api/v1/tasks", body, idempotencyKey: key));
        world.ConcurrentResponses.AddRange(responses);
    }

    // ---- Assertions ----

    [Then(@"^the response is identical to the first one, including where it points$")]
    public void ThenTheResponseIsIdenticalToTheFirst() => AssertReplay();

    [Then(@"^the second response replays the first$")]
    public void ThenTheSecondResponseReplaysTheFirst() => AssertReplay();

    [Then(@"^only one (task|comment|attachment|board|project|membership|webhook|user|organization) was created$")]
    public async Task ThenOnlyOneWasCreated(string kind)
    {
        Assert.True(world.CountsBeforeCreate.TryGetValue(kind, out int before), "No counts were captured before the first attempt.");
        Assert.Equal(before + 1, await seeder.CountAsync(kind));
    }

    [Then(@"^board ""([^""]+)"" has exactly one task ""([^""]+)""$")]
    public async Task ThenTheBoardHasExactlyOneTask(string board, string title) =>
        Assert.Equal(1, await seeder.CountTasksOnBoardAsync(world.BoardNamed(board), title));

    [Then(@"^board ""([^""]+)"" has (\d+) tasks ""([^""]+)""$")]
    public async Task ThenTheBoardHasTasks(string board, int count, string title) =>
        Assert.Equal(count, await seeder.CountTasksOnBoardAsync(world.BoardNamed(board), title));

    [Then(@"^board ""([^""]+)"" has no task ""([^""]+)""$")]
    public async Task ThenTheBoardHasNoTaskNamed(string board, string title) =>
        Assert.Equal(0, await seeder.CountTasksOnBoardAsync(world.BoardNamed(board), title));

    [Then(@"^the error explains the key was already used with a different request$")]
    public void ThenTheErrorExplainsTheKeyReuse()
    {
        Assert.Equal("Idempotency key conflict", world.Response.Json.GetProperty("title").GetString());
        Assert.Contains("different request", world.Response.Json.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Then(@"^neither response reports a server error$")]
    public void ThenNeitherResponseReportsAServerError()
    {
        Assert.Equal(2, world.ConcurrentResponses.Count);
        Assert.All(world.ConcurrentResponses, r => Assert.True((int)r.StatusCode < 500, r.ToString()));
    }

    // ---- Helpers ----

    private Task RunWithKeyAsync(string step, string? key)
    {
        world.NextRequest.IdempotencyKey = key;
        return runner.WhenAsync(step, null, null, "When ");
    }

    private async Task CaptureCountsAsync()
    {
        foreach (string kind in Seeder.CountableKinds)
        {
            world.CountsBeforeCreate[kind] = await seeder.CountAsync(kind);
        }
    }

    /// <summary>Same status, same body (so the same resource and identifier), same Location.</summary>
    private void AssertReplay()
    {
        ApiResponse first = world.PreviousResponse ?? throw new InvalidOperationException("Only one response was recorded.");
        ApiResponse second = world.Response;

        Assert.True(second.IsSuccess, second.ToString());
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(first.Body, second.Body);
        Assert.Equal(first.Location, second.Location);
    }
}
