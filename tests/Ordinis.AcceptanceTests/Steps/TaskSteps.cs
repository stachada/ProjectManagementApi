using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Tasks;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Task actions ("When Bob moves the task …") and task read-back assertions. Writes state the
/// task's current version unless the step says otherwise ("using the version Bob read",
/// "without stating a version").
/// </summary>
[Binding]
internal sealed class TaskSteps(World world, Seeder seeder, ApiDriver api)
{
    /// <summary>A named persona, or the anonymous caller.</summary>
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>A status value the API's enum does not define.</summary>
    private const int UnknownStatus = 99;

    // ---- Arrangement through the API ("has read", "has since …", "has …ed") ----

    [Given(@"^(\w+) has read the task ""([^""]+)""$")]
    public async Task GivenPersonaHasReadTheTask(string persona, string title)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        Assert.True(response.IsSuccess, $"{persona} could not read '{title}': {response}");
        Assert.NotNull(response.ETag);
        world.ReadVersions[(persona, taskId)] = response.ETag;
    }

    [Given(@"^(\w+) has since moved the task ""([^""]+)"" to (\w+)$")]
    public async Task GivenPersonaHasSinceMovedTheTask(string persona, string title, ProjectTaskStatus status)
    {
        ApiResponse response = await MoveAsync(persona, title, status, await CurrentVersionAsync(title));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not move '{title}' to {status}: {response}");
    }

    [Given(@"^(\w+) has reopened the task ""([^""]+)""$")]
    public async Task GivenPersonaHasReopenedTheTask(string persona, string title)
    {
        ApiResponse response = await PostTransitionAsync(persona, title, "reopen");
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not reopen '{title}': {response}");
    }

    // ---- Actions ----

    [When(@"^(\w+) creates a task ""([^""]+)"" on board ""([^""]+)""$")]
    public async Task WhenPersonaCreatesATask(string persona, string title, string board)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/tasks", new
        {
            boardId = world.BoardNamed(board),
            title,
            priority = Priority.Medium,
        });

        if (response.IsSuccess)
        {
            world.Tasks[title] = response.Json.GetProperty("id").GetGuid();
        }
    }

    [When("^" + Actor + @" moves the task ""([^""]+)"" to (\w+)$")]
    public async Task WhenPersonaMovesTheTask(string persona, string title, ProjectTaskStatus status) =>
        await MoveAsync(persona, title, status, await CurrentVersionAsync(title));

    [When("^" + Actor + @" moves the task ""([^""]+)"" to an unknown status$")]
    public async Task WhenPersonaMovesTheTaskToAnUnknownStatus(string persona, string title) =>
        await MoveAsync(persona, title, UnknownStatus, await CurrentVersionAsync(title));

    [When("^" + Actor + @" moves the task ""([^""]+)"" to (\w+) using the version (\w+) read$")]
    public async Task WhenPersonaMovesTheTaskWithReadVersion(string persona, string title, ProjectTaskStatus status, string reader) =>
        await MoveAsync(persona, title, status, VersionReadBy(reader, title));

    [When("^" + Actor + @" moves the task ""([^""]+)"" to (\w+) without stating a version$")]
    public async Task WhenPersonaMovesTheTaskWithoutVersion(string persona, string title, ProjectTaskStatus status) =>
        await MoveAsync(persona, title, status, ifMatch: null);

    [When("^" + Actor + @" closes the task ""([^""]+)""$")]
    public async Task WhenPersonaClosesTheTask(string persona, string title) =>
        await PostTransitionAsync(persona, title, "close");

    [When("^" + Actor + @" reopens the task ""([^""]+)""$")]
    public async Task WhenPersonaReopensTheTask(string persona, string title) =>
        await PostTransitionAsync(persona, title, "reopen");

    [When("^" + Actor + @" renames the task ""([^""]+)"" to ""([^""]+)""$")]
    public async Task WhenPersonaRenamesTheTask(string persona, string title, string newTitle)
    {
        Guid taskId = world.TaskNamed(title);
        ProjectTask? current = await seeder.FindTaskAsync(taskId);

        // UpdateTask replaces every editable field, so carry over everything except the title.
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/tasks/{taskId}", new
        {
            title = newTitle,
            description = current?.Description,
            priority = current?.Priority ?? Priority.Medium,
            dueDate = current?.DueDate,
        }, await CurrentVersionAsync(title));

        if (response.IsSuccess)
        {
            world.Tasks[newTitle] = taskId;
        }
    }

    [When("^" + Actor + @" assigns the task ""([^""]+)"" to (\w+)$")]
    public async Task WhenPersonaAssignsTheTask(string persona, string title, string assignee)
    {
        Guid taskId = world.TaskNamed(title);
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{taskId}/assign",
            new { assigneeId = world.PersonaNamed(assignee).UserId },
            await CurrentVersionAsync(title));
    }

    [When("^" + Actor + @" unassigns the task ""([^""]+)""$")]
    public async Task WhenPersonaUnassignsTheTask(string persona, string title) =>
        await PostTransitionAsync(persona, title, "unassign");

    [When("^" + Actor + @" comments ""([^""]+)"" on the task ""([^""]+)""$")]
    public async Task WhenPersonaComments(string persona, string content, string title)
    {
        Guid taskId = world.TaskNamed(title);
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{taskId}/comments", new { content });
    }

    // ---- Assertions ----

    [Then(@"^the task ""([^""]+)"" (?:has|still has) status (\w+)$")]
    public async Task ThenTheTaskHasStatus(string title, ProjectTaskStatus expected)
    {
        ProjectTask? task = await seeder.FindTaskAsync(world.TaskNamed(title));
        Assert.NotNull(task);
        Assert.Equal(expected, task.Status);
    }

    // ---- Helpers ----

    private Task<ApiResponse> MoveAsync(string persona, string title, object status, string? ifMatch) =>
        api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{world.TaskNamed(title)}/move", new { status }, ifMatch);

    private async Task<ApiResponse> PostTransitionAsync(string persona, string title, string transition) =>
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{world.TaskNamed(title)}/{transition}",
            body: null, await CurrentVersionAsync(title));

    private Task<string> CurrentVersionAsync(string title) =>
        seeder.CurrentTaskVersionAsync(world.TaskNamed(title));

    private string VersionReadBy(string reader, string title) =>
        world.ReadVersions.TryGetValue((reader, world.TaskNamed(title)), out string? version)
            ? version
            : throw new InvalidOperationException($"{reader} has not read the task '{title}' in this scenario.");
}
