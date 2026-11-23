using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Tasks;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Task actions ("When Bob moves the task …") and the arrangement steps built from them
/// ("Given Bob has renamed …"). Writes state the task's current version unless the step says
/// otherwise ("using the version Bob read", "without stating a version"). Read-back assertions
/// live in <see cref="TaskDetailSteps"/>; comments and attachments in their own classes.
/// </summary>
[Binding]
internal sealed class TaskSteps(World world, Seeder seeder, ApiDriver api)
{
    /// <summary>A named persona, or the anonymous caller.</summary>
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>An enum value the API does not define.</summary>
    private const int UnknownEnumValue = 99;

    /// <summary>Which version a write states in <c>If-Match</c>.</summary>
    private enum VersionMode { Current, ReadBy, None }

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

    [Given(@"^(\w+) has (?:since )?moved the task ""([^""]+)"" to (\w+)$")]
    public async Task GivenPersonaHasSinceMovedTheTask(string persona, string title, ProjectTaskStatus status)
    {
        ApiResponse response = await MoveAsync(persona, title, status, await CurrentVersionAsync(title));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not move '{title}' to {status}: {response}");
    }

    [Given(@"^(\w+) has reopened the task ""([^""]+)""$")]
    public async Task GivenPersonaHasReopenedTheTask(string persona, string title)
    {
        ApiResponse response = await PostTransitionAsync(persona, title, "reopen", VersionMode.Current, reader: null);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not reopen '{title}': {response}");
    }

    [Given(@"^(\w+) has deleted the task ""([^""]+)""$")]
    public async Task GivenPersonaHasDeletedTheTask(string persona, string title)
    {
        ApiResponse response = await DeleteAsync(persona, title, VersionMode.Current, reader: null);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not delete '{title}': {response}");
    }

    [Given(@"^(\w+) has (?:since )?renamed the task ""([^""]+)"" to ""([^""]+)""$")]
    public async Task GivenPersonaHasRenamedTheTask(string persona, string title, string newTitle)
    {
        ApiResponse response = await RenameAsync(persona, title, newTitle, VersionMode.Current, reader: null);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not rename '{title}': {response}");
    }

    [Given(@"^(\w+) has (?:since )?assigned the task ""([^""]+)"" to (\w+)$")]
    public async Task GivenPersonaHasAssignedTheTask(string persona, string title, string assignee)
    {
        ApiResponse response = await AssignAsync(persona, title, world.PersonaNamed(assignee).UserId, VersionMode.Current, reader: null);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not assign '{title}' to {assignee}: {response}");
    }

    [Given(@"^(\w+) has unassigned the task ""([^""]+)""$")]
    public async Task GivenPersonaHasUnassignedTheTask(string persona, string title)
    {
        ApiResponse response = await PostTransitionAsync(persona, title, "unassign", VersionMode.Current, reader: null);
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not unassign '{title}': {response}");
    }

    [Given(@"^(\w+) has been removed from project ""([^""]+)""$")]
    public Task GivenPersonaHasBeenRemovedFromProject(string persona, string project) =>
        seeder.RemoveProjectMemberAsync(world.ProjectNamed(project), world.PersonaNamed(persona).UserId);

    // ---- Creation ----

    [When("^" + Actor + @" creates a task ""([^""]+)"" on board ""([^""]+)""$")]
    public Task WhenPersonaCreatesATask(string persona, string title, string board) =>
        CreateTaskAsync(persona, board, new { boardId = world.BoardNamed(board), title, priority = Priority.Medium }, title);

    [When("^" + Actor + @" creates a task ""([^""]+)"" on board ""([^""]+)"" with priority (\w+)$")]
    public Task WhenPersonaCreatesATaskWithPriority(string persona, string title, string board, Priority priority) =>
        CreateTaskAsync(persona, board, new { boardId = world.BoardNamed(board), title, priority }, title);

    [When("^" + Actor + @" creates a task ""([^""]+)"" on board ""([^""]+)"" with an unknown priority$")]
    public Task WhenPersonaCreatesATaskWithUnknownPriority(string persona, string title, string board) =>
        CreateTaskAsync(persona, board, new { boardId = world.BoardNamed(board), title, priority = UnknownEnumValue }, title);

    [When("^" + Actor + @" creates a task ""([^""]+)"" on board ""([^""]+)"" due (yesterday|now)$")]
    public Task WhenPersonaCreatesATaskDue(string persona, string title, string board, string when) =>
        CreateTaskAsync(persona, board, new
        {
            boardId = world.BoardNamed(board),
            title,
            priority = Priority.Medium,
            dueDate = when == "yesterday" ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow,
        }, title);

    [When("^" + Actor + @" creates a task ""([^""]+)"" on board ""([^""]+)"" assigned to (\w+)$")]
    public Task WhenPersonaCreatesAnAssignedTask(string persona, string title, string board, string assignee) =>
        CreateTaskAsync(persona, board, new { boardId = world.BoardNamed(board), title, priority = Priority.Medium, assigneeId = world.PersonaNamed(assignee).UserId }, title);

    [When("^" + Actor + @" creates a task ""([^""]+)"" on board ""([^""]+)"" assigned to an unknown user$")]
    public Task WhenPersonaCreatesATaskAssignedToUnknownUser(string persona, string title, string board) =>
        CreateTaskAsync(persona, board, new { boardId = world.BoardNamed(board), title, priority = Priority.Medium, assigneeId = Guid.CreateVersion7() }, title);

    [When("^" + Actor + @" creates a task ""([^""]+)"" on an unknown board$")]
    public Task WhenPersonaCreatesATaskOnUnknownBoard(string persona, string title) =>
        CreateTaskAsync(persona, board: null, new { boardId = Guid.CreateVersion7(), title, priority = Priority.Medium }, title);

    [When("^" + Actor + @" creates a task (without a title|with a blank title|with a \d+-character title) on board ""([^""]+)""$")]
    public Task WhenPersonaCreatesATaskWithTitleVariant(string persona, string variant, string board)
    {
        string? title = variant switch
        {
            "without a title" => null,
            "with a blank title" => "   ",
            _ => new string('t', int.Parse(System.Text.RegularExpressions.Regex.Match(variant, @"\d+").Value, System.Globalization.CultureInfo.InvariantCulture)),
        };
        return CreateTaskAsync(persona, board, new { boardId = world.BoardNamed(board), title, priority = Priority.Medium }, title);
    }

    /// <summary>A two-column "field | value" table: title, description, priority, assignee, due date.</summary>
    [When("^" + Actor + @" creates a task on board ""([^""]+)"" with:$")]
    public Task WhenPersonaCreatesATaskWithDetails(string persona, string board, DataTable details)
    {
        Dictionary<string, string> fields = Fields(details);
        return CreateTaskAsync(persona, board, new
        {
            boardId = world.BoardNamed(board),
            title = fields["title"],
            description = fields.GetValueOrDefault("description"),
            priority = fields.TryGetValue("priority", out string? p) ? Enum.Parse<Priority>(p) : Priority.Medium,
            assigneeId = fields.TryGetValue("assignee", out string? a) ? world.PersonaNamed(a).UserId : (Guid?)null,
            dueDate = fields.TryGetValue("due date", out string? due) ? DaysFromNow(due) : (DateTimeOffset?)null,
        }, fields["title"]);
    }

    // ---- Updates ----

    /// <summary>A two-column "field | value" table: title, description, priority, due date. Omitted fields are cleared.</summary>
    [When("^" + Actor + @" updates the task ""([^""]+)"" with:$")]
    public async Task WhenPersonaUpdatesTheTaskWithDetails(string persona, string title, DataTable details)
    {
        Dictionary<string, string> fields = Fields(details);
        ProjectTask current = await CurrentAsync(title);
        await UpdateAsync(persona, title, new
        {
            title = fields.GetValueOrDefault("title") ?? current.Title,
            description = fields.GetValueOrDefault("description"),
            priority = fields.TryGetValue("priority", out string? p) ? Enum.Parse<Priority>(p) : current.Priority,
            dueDate = fields.TryGetValue("due date", out string? due) ? DaysFromNow(due) : (DateTimeOffset?)null,
        }, fields.GetValueOrDefault("title"), VersionMode.Current, reader: null);
    }

    [When("^" + Actor + @" updates the task ""([^""]+)"" with only a title ""([^""]+)"" and priority (\w+)$")]
    public Task WhenPersonaUpdatesTheTaskWithOnlyTitleAndPriority(string persona, string title, string newTitle, Priority priority) =>
        UpdateAsync(persona, title, new { title = newTitle, priority }, newTitle, VersionMode.Current, reader: null);

    [When("^" + Actor + @" updates the task ""([^""]+)"" with (a blank title|a \d+-character title|an unknown priority|a due date of yesterday)$")]
    public async Task WhenPersonaUpdatesTheTaskWithInvalidValue(string persona, string title, string variant)
    {
        ProjectTask current = await CurrentAsync(title);
        object body = variant switch
        {
            "a blank title" => new { title = "   ", description = current.Description, priority = current.Priority, dueDate = current.DueDate },
            "an unknown priority" => new { title = current.Title, description = current.Description, priority = UnknownEnumValue, dueDate = current.DueDate },
            "a due date of yesterday" => new { title = current.Title, description = current.Description, priority = current.Priority, dueDate = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-1) },
            _ => new { title = new string('t', int.Parse(System.Text.RegularExpressions.Regex.Match(variant, @"\d+").Value, System.Globalization.CultureInfo.InvariantCulture)), description = current.Description, priority = current.Priority, dueDate = current.DueDate },
        };
        await UpdateAsync(persona, title, body, newTitle: null, VersionMode.Current, reader: null);
    }

    [When("^" + Actor + @" renames the task ""([^""]+)"" to ""([^""]+)""$")]
    public Task WhenPersonaRenamesTheTask(string persona, string title, string newTitle) =>
        RenameAsync(persona, title, newTitle, VersionMode.Current, reader: null);

    [When("^" + Actor + @" renames the task ""([^""]+)"" to ""([^""]+)"" using the version (\w+) read$")]
    public Task WhenPersonaRenamesTheTaskWithReadVersion(string persona, string title, string newTitle, string reader) =>
        RenameAsync(persona, title, newTitle, VersionMode.ReadBy, reader);

    [When("^" + Actor + @" renames the task ""([^""]+)"" to ""([^""]+)"" without stating a version$")]
    public Task WhenPersonaRenamesTheTaskWithoutVersion(string persona, string title, string newTitle) =>
        RenameAsync(persona, title, newTitle, VersionMode.None, reader: null);

    [When("^" + Actor + @" changes the priority of the task ""([^""]+)"" to (\w+)$")]
    public async Task WhenPersonaChangesThePriority(string persona, string title, Priority priority)
    {
        ProjectTask current = await CurrentAsync(title);
        await UpdateAsync(persona, title,
            new { title = current.Title, description = current.Description, priority, dueDate = current.DueDate },
            newTitle: null, VersionMode.Current, reader: null);
    }

    // ---- Deletion ----

    [When("^" + Actor + @" deletes the task ""([^""]+)""$")]
    public Task WhenPersonaDeletesTheTask(string persona, string title) =>
        DeleteAsync(persona, title, VersionMode.Current, reader: null);

    [When("^" + Actor + @" deletes the task ""([^""]+)"" using the version (\w+) read$")]
    public Task WhenPersonaDeletesTheTaskWithReadVersion(string persona, string title, string reader) =>
        DeleteAsync(persona, title, VersionMode.ReadBy, reader);

    [When("^" + Actor + @" deletes the task ""([^""]+)"" without stating a version$")]
    public Task WhenPersonaDeletesTheTaskWithoutVersion(string persona, string title) =>
        DeleteAsync(persona, title, VersionMode.None, reader: null);

    // ---- Workflow ----

    [When("^" + Actor + @" moves the task ""([^""]+)"" to (\w+)$")]
    public async Task WhenPersonaMovesTheTask(string persona, string title, ProjectTaskStatus status) =>
        await MoveAsync(persona, title, status, await CurrentVersionAsync(title));

    [When("^" + Actor + @" moves the task ""([^""]+)"" to an unknown status$")]
    public async Task WhenPersonaMovesTheTaskToAnUnknownStatus(string persona, string title) =>
        await MoveAsync(persona, title, UnknownEnumValue, await CurrentVersionAsync(title));

    [When("^" + Actor + @" moves the task ""([^""]+)"" to (\w+) using the version (\w+) read$")]
    public async Task WhenPersonaMovesTheTaskWithReadVersion(string persona, string title, ProjectTaskStatus status, string reader) =>
        await MoveAsync(persona, title, status, VersionReadBy(reader, title));

    [When("^" + Actor + @" moves the task ""([^""]+)"" to (\w+) without stating a version$")]
    public async Task WhenPersonaMovesTheTaskWithoutVersion(string persona, string title, ProjectTaskStatus status) =>
        await MoveAsync(persona, title, status, ifMatch: null);

    [When("^" + Actor + @" closes the task ""([^""]+)""$")]
    public Task WhenPersonaClosesTheTask(string persona, string title) =>
        PostTransitionAsync(persona, title, "close", VersionMode.Current, reader: null);

    [When("^" + Actor + @" reopens the task ""([^""]+)""$")]
    public Task WhenPersonaReopensTheTask(string persona, string title) =>
        PostTransitionAsync(persona, title, "reopen", VersionMode.Current, reader: null);

    // ---- Assignment ----

    [When("^" + Actor + @" assigns the task ""([^""]+)"" to (\w+)$")]
    public Task WhenPersonaAssignsTheTask(string persona, string title, string assignee) =>
        AssignAsync(persona, title, world.PersonaNamed(assignee).UserId, VersionMode.Current, reader: null);

    [When("^" + Actor + @" assigns the task ""([^""]+)"" to (\w+) using the version (\w+) read$")]
    public Task WhenPersonaAssignsTheTaskWithReadVersion(string persona, string title, string assignee, string reader) =>
        AssignAsync(persona, title, world.PersonaNamed(assignee).UserId, VersionMode.ReadBy, reader);

    [When("^" + Actor + @" assigns the task ""([^""]+)"" to an unknown user$")]
    public Task WhenPersonaAssignsTheTaskToUnknownUser(string persona, string title) =>
        AssignAsync(persona, title, Guid.CreateVersion7(), VersionMode.Current, reader: null);

    [When("^" + Actor + @" unassigns the task ""([^""]+)""$")]
    public Task WhenPersonaUnassignsTheTask(string persona, string title) =>
        PostTransitionAsync(persona, title, "unassign", VersionMode.Current, reader: null);

    [When("^" + Actor + @" unassigns the task ""([^""]+)"" without stating a version$")]
    public Task WhenPersonaUnassignsTheTaskWithoutVersion(string persona, string title) =>
        PostTransitionAsync(persona, title, "unassign", VersionMode.None, reader: null);

    // ---- Helpers ----

    private async Task CreateTaskAsync(string persona, string? board, object body, string? title)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/tasks", body);

        if (response.IsSuccess && title is not null)
        {
            Guid taskId = response.Json.GetProperty("id").GetGuid();
            world.Tasks[title] = taskId;
            world.TaskCreationOrder.Add(taskId);
        }
    }

    private async Task<ApiResponse> UpdateAsync(string persona, string title, object body, string? newTitle, VersionMode mode, string? reader)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Put, $"/api/v1/tasks/{taskId}", body,
            await VersionAsync(title, mode, reader));

        if (response.IsSuccess && newTitle is not null)
        {
            world.Tasks[newTitle] = taskId;
        }

        return response;
    }

    private async Task<ApiResponse> RenameAsync(string persona, string title, string newTitle, VersionMode mode, string? reader)
    {
        // UpdateTask replaces every editable field, so carry over everything except the title.
        ProjectTask? current = await seeder.FindTaskAsync(world.TaskNamed(title));
        return await UpdateAsync(persona, title, new
        {
            title = newTitle,
            description = current?.Description,
            priority = current?.Priority ?? Priority.Medium,
            dueDate = current?.DueDate,
        }, newTitle, mode, reader);
    }

    private async Task<ApiResponse> DeleteAsync(string persona, string title, VersionMode mode, string? reader) =>
        await api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/tasks/{world.TaskNamed(title)}",
            body: null, await VersionAsync(title, mode, reader));

    private async Task<ApiResponse> AssignAsync(string persona, string title, Guid assigneeId, VersionMode mode, string? reader) =>
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{world.TaskNamed(title)}/assign",
            new { assigneeId }, await VersionAsync(title, mode, reader));

    private Task<ApiResponse> MoveAsync(string persona, string title, object status, string? ifMatch) =>
        api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{world.TaskNamed(title)}/move", new { status }, ifMatch);

    private async Task<ApiResponse> PostTransitionAsync(string persona, string title, string transition, VersionMode mode, string? reader) =>
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{world.TaskNamed(title)}/{transition}",
            body: null, await VersionAsync(title, mode, reader));

    private async Task<string?> VersionAsync(string title, VersionMode mode, string? reader) => mode switch
    {
        VersionMode.Current => await CurrentVersionAsync(title),
        VersionMode.ReadBy => VersionReadBy(reader!, title),
        _ => null,
    };

    private Task<string> CurrentVersionAsync(string title) =>
        seeder.CurrentTaskVersionAsync(world.TaskNamed(title));

    private string VersionReadBy(string reader, string title) =>
        world.ReadVersions.TryGetValue((reader, world.TaskNamed(title)), out string? version)
            ? version
            : throw new InvalidOperationException($"{reader} has not read the task '{title}' in this scenario.");

    private async Task<ProjectTask> CurrentAsync(string title) =>
        await seeder.FindTaskAsync(world.TaskNamed(title))
            ?? throw new InvalidOperationException($"Task '{title}' does not exist.");

    /// <summary>"7 days from now" → now + 7 days.</summary>
    private static DateTimeOffset DaysFromNow(string phrase) =>
        DateTimeOffset.UtcNow.AddDays(int.Parse(phrase.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A two-column table without a header row (Reqnroll reads the first line as the header).</summary>
    internal static Dictionary<string, string> Fields(DataTable table) =>
        new[] { (table.Header.ElementAt(0), table.Header.ElementAt(1)) }
            .Concat(table.Rows.Select(r => (r[0], r[1])))
            .ToDictionary(f => f.Item1.Trim(), f => f.Item2.Trim(), StringComparer.Ordinal);
}
