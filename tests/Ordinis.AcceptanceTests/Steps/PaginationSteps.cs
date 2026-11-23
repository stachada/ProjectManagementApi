using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Paging over any list endpoint. A "list phrase" from the feature files ("all tasks",
/// "the tasks of Bob", "the audit trail of project \"Apollo\"", …) is resolved to its route by
/// <see cref="PathFor"/>, so one set of steps covers every paginated endpoint.
/// </summary>
[Binding]
internal sealed class PaginationSteps(World world, Seeder seeder, ApiDriver api)
{
    private readonly List<Guid> _allPagesIds = [];

    // ---- Arrangement ----

    [Given(@"^(\d+) tasks on board ""([^""]+)"" assigned to (\w+)$")]
    public async Task GivenManyAssignedTasks(int count, string board, string assignee)
    {
        Guid assigneeId = world.PersonaNamed(assignee).UserId;
        for (int i = 1; i <= count; i++)
        {
            Guid taskId = await seeder.CreateTaskAsync(world.BoardNamed(board), $"Generated task {i:000}");
            await seeder.AssignTaskAsync(taskId, assigneeId);
        }
    }

    [Given(@"^(\d+) tasks on board ""([^""]+)"" in total$")]
    public async Task GivenTasksInTotal(int total, string board)
    {
        for (int i = world.TaskCreationOrder.Count + 1; i <= total; i++)
        {
            await seeder.CreateTaskAsync(world.BoardNamed(board), $"Generated task {i:000}");
        }
    }

    // ---- Actions: lists not covered by ListingSteps ----

    [When(@"^(\w+) lists the tasks of (\w+)$")]
    public Task WhenListsTheTasksOfUser(string persona, string user) =>
        api.SendAsync(persona, HttpMethod.Get, PathFor($"the tasks of {user}"));

    [When(@"^(\w+) lists the projects$")]
    public Task WhenListsTheProjects(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, PathFor("the projects"));

    [When(@"^(\w+) lists the projects of organization ""([^""]+)""$")]
    public Task WhenListsTheOrganizationProjects(string persona, string organization) =>
        api.SendAsync(persona, HttpMethod.Get, PathFor($"the projects of organization \"{organization}\""));

    [When(@"^(\w+) lists the audit trail of project ""([^""]+)""$")]
    public Task WhenListsTheAuditTrail(string persona, string project) =>
        api.SendAsync(persona, HttpMethod.Get, PathFor($"the audit trail of project \"{project}\""));

    [When(@"^(\w+) lists the search results for ""([^""]+)""$")]
    public Task WhenListsTheSearchResults(string persona, string term) =>
        api.SendAsync(persona, HttpMethod.Get, PathFor($"the search results for \"{term}\""));

    [When(@"^(\w+) lists (.+), page (-?\d+) with (-?\d+) per page$")]
    public Task WhenListsAPage(string persona, string list, int page, int pageSize) =>
        api.SendAsync(persona, HttpMethod.Get, WithQuery(PathFor(list), $"page={page}&pageSize={pageSize}"));

    [When(@"^(\w+) reads every page of all tasks with (\d+) per page$")]
    public async Task WhenReadsEveryPage(string persona, int pageSize)
    {
        for (int page = 1; page <= 1000; page++)
        {
            ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks?page={page}&pageSize={pageSize}");
            Assert.True(response.IsSuccess, response.ToString());

            List<Guid> ids = response.Json.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
            if (ids.Count == 0)
            {
                return;
            }

            _allPagesIds.AddRange(ids);
        }

        Assert.Fail("Paging never reached an empty page.");
    }

    // ---- Assertions ----

    [Then(@"^(\d+) tasks are listed$")]
    [Then(@"^(\d+) (?:entry is|entries are) listed$")]
    public void ThenCountIsListed(int expected)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal(expected, world.Response.Json.GetArrayLength());
    }

    [Then(@"^the first (\d+) tasks are listed$")]
    public void ThenTheFirstTasksAreListed(int count)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        List<Guid> listed = world.Response.Json.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(world.TaskCreationOrder.Take(count), listed);
    }

    /// <summary>A sparse fieldset: the list phrase, then the raw <c>fields</c> value, sent as written.</summary>
    [When(@"^(\w+) lists (.+) returning only ""([^""]*)""$")]
    public Task WhenListsReturningOnly(string persona, string list, string fields) =>
        api.SendAsync(persona, HttpMethod.Get, WithQuery(PathFor(list), $"fields={Uri.EscapeDataString(fields)}"));

    [Then(@"^every task appears exactly once$")]
    public void ThenEveryTaskAppearsExactlyOnce()
    {
        Assert.Equal(_allPagesIds.Count, _allPagesIds.Distinct().Count());
        Assert.Equal(world.TaskCreationOrder.OrderBy(id => id), _allPagesIds.OrderBy(id => id));
    }

    [Then(@"^the total count is reported$")]
    public void ThenTheTotalCountIsReported()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.NotNull(world.Response.TotalCount);
    }

    // ---- Helpers ----

    /// <summary>Resolves a list phrase from the feature files to its API route.</summary>
    private string PathFor(string list)
    {
        if (list == "all tasks")
        {
            return "/api/v1/tasks";
        }

        if (list == "the projects")
        {
            return "/api/v1/projects";
        }

        string? quoted = list.Contains('"') ? list.Split('"')[1] : null;

        if (list.StartsWith("the tasks of project ", StringComparison.Ordinal))
        {
            return $"/api/v1/projects/{world.ProjectNamed(quoted!)}/tasks";
        }

        if (list.StartsWith("the tasks of board ", StringComparison.Ordinal))
        {
            return $"/api/v1/boards/{world.BoardNamed(quoted!)}/tasks";
        }

        if (list.StartsWith("the projects of organization ", StringComparison.Ordinal))
        {
            return $"/api/v1/organizations/{world.OrganizationNamed(quoted!)}/projects";
        }

        if (list.StartsWith("the audit trail of project ", StringComparison.Ordinal))
        {
            return $"/api/v1/projects/{world.ProjectNamed(quoted!)}/audit";
        }

        if (list.StartsWith("the search results for ", StringComparison.Ordinal))
        {
            return $"/api/v1/search?type=tasks&q={Uri.EscapeDataString(quoted!)}";
        }

        if (list.StartsWith("the tasks of ", StringComparison.Ordinal))
        {
            return $"/api/v1/users/{world.PersonaNamed(list["the tasks of ".Length..]).UserId}/tasks";
        }

        throw new InvalidOperationException($"Unknown list '{list}'.");
    }

    private static string WithQuery(string path, string query) =>
        path.Contains('?') ? $"{path}&{query}" : $"{path}?{query}";
}
