using System.Text.Json;
using System.Text.RegularExpressions;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Tasks;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Viewing tasks, read-back assertions on task state, and the hypermedia (<c>_links</c>) contract.
/// State assertions read the database directly (through the domain model); list-membership and
/// representation assertions go through the API as a hidden org-Admin observer.
/// </summary>
[Binding]
internal sealed partial class TaskDetailSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    // ---- Viewing ----

    [When("^" + Actor + @" views the task ""([^""]+)""$")]
    public Task WhenPersonaViewsTheTask(string persona, string title) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{world.TaskNamed(title)}");

    [When("^" + Actor + @" views an unknown task$")]
    public Task WhenPersonaViewsAnUnknownTask(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{Guid.CreateVersion7()}");

    /// <summary>Follows a link from the task's current representation, stating the current version.</summary>
    [When("^" + Actor + @" follows the ""([^""]+)"" link of the task ""([^""]+)""$")]
    public async Task WhenPersonaFollowsALink(string persona, string rel, string title)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse task = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        JsonElement link = Links(task).Single(l => l.GetProperty("rel").GetString() == rel);

        // A move:{status} link targets the generic move endpoint; the status comes from the rel.
        object? body = rel.StartsWith("move:", StringComparison.Ordinal)
            ? new { status = Enum.Parse<ProjectTaskStatus>(rel["move:".Length..], ignoreCase: true) }
            : null;

        await api.SendAsync(persona, new HttpMethod(link.GetProperty("method").GetString()!), link.GetProperty("href").GetString()!,
            body, await seeder.CurrentTaskVersionAsync(taskId));
    }

    // ---- Task state ----

    [Then(@"^the task ""([^""]+)"" (?:has|still has) status (\w+)$")]
    public async Task ThenTheTaskHasStatus(string title, ProjectTaskStatus expected) =>
        Assert.Equal(expected, (await TaskAsync(title)).Status);

    [Then(@"^the task ""([^""]+)"" (?:has|still has) priority (\w+)$")]
    public async Task ThenTheTaskHasPriority(string title, Priority expected) =>
        Assert.Equal(expected, (await TaskAsync(title)).Priority);

    [Then(@"^the task ""([^""]+)"" has description ""([^""]+)""$")]
    public async Task ThenTheTaskHasDescription(string title, string expected) =>
        Assert.Equal(expected, (await TaskAsync(title)).Description);

    [Then(@"^the task ""([^""]+)"" is due (\d+) days from now$")]
    public async Task ThenTheTaskIsDue(string title, int days)
    {
        DateTimeOffset? due = (await TaskAsync(title)).DueDate;
        Assert.NotNull(due);
        Assert.InRange(due.Value, TestHost.Clock.GetUtcNow().AddDays(days).AddMinutes(-10), TestHost.Clock.GetUtcNow().AddDays(days).AddMinutes(10));
    }

    [Then(@"^the task ""([^""]+)"" has no description and no due date$")]
    public async Task ThenTheTaskHasNoDescriptionOrDueDate(string title)
    {
        ProjectTask task = await TaskAsync(title);
        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
    }

    [Then(@"^the task ""([^""]+)"" is assigned to (\w+)$")]
    public async Task ThenTheTaskIsAssignedTo(string title, string assignee) =>
        Assert.Equal(world.PersonaNamed(assignee).UserId, (await TaskAsync(title)).AssigneeId);

    [Then(@"^the task ""([^""]+)"" has no assignee$")]
    public async Task ThenTheTaskHasNoAssignee(string title) =>
        Assert.Null((await TaskAsync(title)).AssigneeId);

    [Then(@"^the task ""([^""]+)"" exists$")]
    public async Task ThenTheTaskExists(string title) =>
        Assert.NotNull(await seeder.FindTaskAsync(world.TaskNamed(title)));

    /// <summary>
    /// Through the API, as the org-Admin observer — who can see every live task of the
    /// organization, so a 404 means the task (or its project) is deleted, not merely hidden.
    /// </summary>
    [Then(@"^the task ""([^""]+)"" is not found$")]
    public async Task ThenTheTaskIsNotFound(string title)
    {
        Guid taskId = world.TaskNamed(title);
        Guid observer = await seeder.ObserverForOrganizationAsync(await seeder.OrganizationOfTaskAsync(taskId));
        ApiResponse response = await api.SendAsUserAsync(observer, HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.NotFound, $"Expected 404 for task '{title}', got {response}");
    }

    [Then(@"^the task ""([^""]+)"" is not listed on board ""([^""]+)""$")]
    public async Task ThenTheTaskIsNotListedOnBoard(string title, string board)
    {
        Guid boardId = world.BoardNamed(board);
        Assert.DoesNotContain(title, await TitlesAsync(await seeder.ObserverForBoardAsync(boardId), $"/api/v1/boards/{boardId}/tasks?pageSize=100"));
    }

    [Then(@"^the task ""([^""]+)"" is not listed in project ""([^""]+)""$")]
    public async Task ThenTheTaskIsNotListedInProject(string title, string project)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid observer = await seeder.ObserverForOrganizationAsync(world.ProjectOrganizations[projectId]);
        Assert.DoesNotContain(title, await TitlesAsync(observer, $"/api/v1/projects/{projectId}/tasks?pageSize=100"));
    }

    [Then(@"^the task ""([^""]+)"" (is|is not) listed among the tasks of (\w+)$")]
    public async Task ThenTheTaskIsListedAmongTheTasksOf(string title, string verb, string user)
    {
        World.Persona persona = world.PersonaNamed(user);
        Guid observer = await seeder.ObserverForOrganizationAsync(persona.OrganizationId);
        List<string> titles = await TitlesAsync(observer, $"/api/v1/users/{persona.UserId}/tasks?pageSize=100");

        if (verb == "is")
        {
            Assert.Contains(title, titles);
        }
        else
        {
            Assert.DoesNotContain(title, titles);
        }
    }

    [Then(@"^the response points to the new task$")]
    public void ThenTheResponsePointsToTheNewTask()
    {
        Guid id = world.Response.Json.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/tasks/{id}", world.Response.Location?.AbsolutePath);
    }

    // ---- Representation ----

    [Then(@"^the task details show title, description, status, priority, assignee name, due date, creation and update times$")]
    public void ThenTheTaskDetailsShowTheFields()
    {
        JsonElement task = SuccessfulJson();
        foreach (string field in new[] { "title", "description", "status", "priority", "assigneeDisplayName", "dueDate", "createdAt", "updatedAt" })
        {
            Assert.True(task.TryGetProperty(field, out _), $"Task details are missing '{field}'.");
        }
    }

    [Then(@"^the task details include its comments and attachments$")]
    public void ThenTheTaskDetailsIncludeCommentsAndAttachments()
    {
        JsonElement task = SuccessfulJson();
        Assert.Equal(JsonValueKind.Array, task.GetProperty("comments").ValueKind);
        Assert.Equal(JsonValueKind.Array, task.GetProperty("attachments").ValueKind);
    }

    [Then(@"^the task details state the current version$")]
    public async Task ThenTheTaskDetailsStateTheCurrentVersion()
    {
        JsonElement task = SuccessfulJson();
        string current = await seeder.CurrentTaskVersionAsync(task.GetProperty("id").GetGuid());
        Assert.Equal(current, world.Response.ETag);
        Assert.Equal(current, $"\"{task.GetProperty("concurrencyToken").GetString()}\"");
    }

    [Then(@"^the task shows (\d+) comments?$")]
    public void ThenTheTaskShowsComments(int count) =>
        Assert.Equal(count, SuccessfulJson().GetProperty("comments").GetArrayLength());

    [Then(@"^the task shows (\d+) attachments?$")]
    public void ThenTheTaskShowsAttachments(int count) =>
        Assert.Equal(count, SuccessfulJson().GetProperty("attachments").GetArrayLength());

    /// <summary>A hidden resource and a missing one must be indistinguishable (no existence oracle).</summary>
    [Then(@"^both responses are identical apart from the identifier$")]
    public void ThenBothResponsesAreIdentical()
    {
        ApiResponse first = world.PreviousResponse ?? throw new InvalidOperationException("Only one response was recorded.");
        ApiResponse second = world.Response;
        Assert.Equal(first.StatusCode, second.StatusCode);

        foreach (string field in new[] { "type", "title", "status", "detail" })
        {
            Assert.Equal(Normalized(first.Json, field), Normalized(second.Json, field));
        }
    }

    // ---- Links ----

    [Then(@"^the task has the links:$")]
    public void ThenTheTaskHasTheLinks(DataTable expected)
    {
        List<JsonElement> links = Links(world.Response);
        foreach (DataTableRow row in expected.Rows)
        {
            Assert.Contains(links, l => l.GetProperty("rel").GetString() == row["rel"] && l.GetProperty("method").GetString() == row["method"]);
        }
    }

    [Then(@"^the task has an? ""([^""]+)"" link$")]
    public void ThenTheTaskHasALink(string rel) =>
        Assert.Contains(Links(world.Response), l => l.GetProperty("rel").GetString() == rel);

    [Then(@"^the task has no ""([^""]+)"" link$")]
    public void ThenTheTaskHasNoLink(string rel) =>
        Assert.DoesNotContain(Links(world.Response), l => l.GetProperty("rel").GetString() == rel);

    [Then(@"^the task's move links are ""([^""]+)""$")]
    public void ThenTheTasksMoveLinksAre(string expected)
    {
        IEnumerable<string> actual = Links(world.Response).Select(l => l.GetProperty("rel").GetString()!).Where(r => r.StartsWith("move:", StringComparison.Ordinal));
        Assert.Equal(expected.Split(',', StringSplitOptions.TrimEntries).Order(), actual.Order());
    }

    [Then(@"^the task has no move links$")]
    public void ThenTheTaskHasNoMoveLinks() =>
        Assert.DoesNotContain(Links(world.Response), l => l.GetProperty("rel").GetString()!.StartsWith("move:", StringComparison.Ordinal));

    [Then(@"^the task has a ""board"" link to board ""([^""]+)""$")]
    public void ThenTheTaskHasABoardLink(string board)
    {
        JsonElement link = Links(world.Response).Single(l => l.GetProperty("rel").GetString() == "board");
        Assert.Equal($"/api/v1/boards/{world.BoardNamed(board)}", link.GetProperty("href").GetString());
        Assert.Equal("GET", link.GetProperty("method").GetString());
    }

    [Then(@"^the task has all the links of version 1$")]
    public async Task ThenTheTaskHasAllV1Links()
    {
        JsonElement v2 = SuccessfulJson();
        Guid taskId = v2.GetProperty("id").GetGuid();
        Guid observer = await seeder.ObserverForBoardAsync(v2.GetProperty("boardId").GetGuid());
        ApiResponse v1 = await api.SendAsUserAsync(observer, HttpMethod.Get, $"/api/v1/tasks/{taskId}");

        HashSet<string> v2Links = Links(world.Response).Select(Describe).ToHashSet();
        Assert.All(Links(v1).Select(Describe), l => Assert.Contains(l, v2Links));
    }

    // ---- Helpers ----

    private async Task<ProjectTask> TaskAsync(string title) =>
        await seeder.FindTaskAsync(world.TaskNamed(title))
            ?? throw new InvalidOperationException($"Task '{title}' does not exist.");

    private async Task<List<string>> TitlesAsync(Guid observer, string path)
    {
        ApiResponse response = await api.SendAsUserAsync(observer, HttpMethod.Get, path);
        Assert.True(response.IsSuccess, response.ToString());
        return response.Json.EnumerateArray().Select(e => e.GetProperty("title").GetString()!).ToList();
    }

    private JsonElement SuccessfulJson()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        return world.Response.Json;
    }

    private static List<JsonElement> Links(ApiResponse response)
    {
        Assert.True(response.IsSuccess, response.ToString());
        return response.Json.GetProperty("_links").EnumerateArray().ToList();
    }

    private static string Describe(JsonElement link) =>
        $"{link.GetProperty("rel").GetString()} {link.GetProperty("method").GetString()} {link.GetProperty("href").GetString()}";

    private static string? Normalized(JsonElement problem, string field) =>
        problem.TryGetProperty(field, out JsonElement value)
            ? GuidPattern().Replace(value.ToString(), "<id>")
            : null;

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}
