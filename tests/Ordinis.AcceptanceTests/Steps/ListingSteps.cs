using System.Globalization;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Tasks;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Task lists — across all visible projects, per project and per board — with the filter and
/// sort phrases used by the feature files ("filtered by assignee Bob and priority High",
/// "sorted by dueDate ascending").
/// </summary>
[Binding]
internal sealed class ListingSteps(World world, ApiDriver api)
{
    private readonly List<List<string>> _repeatedReads = [];

    // ---- Actions ----

    [When(@"^(\w+) lists all tasks$")]
    public Task WhenListsAllTasks(string persona) =>
        ListAsync(persona, "/api/v1/tasks", filter: null, sort: null);

    [When(@"^(\w+) lists all tasks filtered by (.+)$")]
    public Task WhenListsAllTasksFiltered(string persona, string filter) =>
        ListAsync(persona, "/api/v1/tasks", filter, sort: null);

    [When(@"^(\w+) lists all tasks sorted by (\w+) (ascending|descending)$")]
    public Task WhenListsAllTasksSorted(string persona, string field, string direction) =>
        ListAsync(persona, "/api/v1/tasks", filter: null, sort: (field, direction));

    [When(@"^(\w+) lists all tasks sorted by (\w+) (ascending|descending), page 1 and page 2 of size (\d+), twice$")]
    public async Task WhenListsTwoPagesTwice(string persona, string field, string direction, int pageSize)
    {
        for (int read = 0; read < 2; read++)
        {
            var titles = new List<string>();
            for (int page = 1; page <= 2; page++)
            {
                await ListAsync(persona, "/api/v1/tasks", filter: null, sort: (field, direction), page, pageSize);
                Assert.True(world.Response.IsSuccess, world.Response.ToString());
                titles.AddRange(ListedTitles());
                titles.Add($"--- end of page {page} ---");
            }

            _repeatedReads.Add(titles);
        }
    }

    [When(@"^(\w+) lists the tasks of project ""([^""]+)""$")]
    public Task WhenListsProjectTasks(string persona, string project) =>
        ListAsync(persona, $"/api/v1/projects/{world.ProjectNamed(project)}/tasks", filter: null, sort: null);

    [When(@"^(\w+) lists the tasks of project ""([^""]+)"" filtered by (.+)$")]
    public Task WhenListsProjectTasksFiltered(string persona, string project, string filter) =>
        ListAsync(persona, $"/api/v1/projects/{world.ProjectNamed(project)}/tasks", filter, sort: null);

    [When(@"^(\w+) lists the tasks of project ""([^""]+)"" sorted by (\w+) (ascending|descending)$")]
    public Task WhenListsProjectTasksSorted(string persona, string project, string field, string direction) =>
        ListAsync(persona, $"/api/v1/projects/{world.ProjectNamed(project)}/tasks", filter: null, sort: (field, direction));

    [When(@"^(\w+) lists the tasks of board ""([^""]+)""$")]
    public Task WhenListsBoardTasks(string persona, string board) =>
        ListAsync(persona, $"/api/v1/boards/{world.BoardNamed(board)}/tasks", filter: null, sort: null);

    [When(@"^(\w+) lists the tasks of board ""([^""]+)"" filtered by (.+)$")]
    public Task WhenListsBoardTasksFiltered(string persona, string board, string filter) =>
        ListAsync(persona, $"/api/v1/boards/{world.BoardNamed(board)}/tasks", filter, sort: null);

    [When(@"^(\w+) lists the tasks of (\w+) filtered by (.+)$")]
    public Task WhenListsUserTasksFiltered(string persona, string user, string filter) =>
        ListAsync(persona, $"/api/v1/users/{world.PersonaNamed(user).UserId}/tasks", filter, sort: null);

    [When(@"^(\w+) lists the tasks of (\w+) sorted by (\w+) (ascending|descending)$")]
    public Task WhenListsUserTasksSorted(string persona, string user, string field, string direction) =>
        ListAsync(persona, $"/api/v1/users/{world.PersonaNamed(user).UserId}/tasks", filter: null, sort: (field, direction));

    [When(@"^(\w+) lists the tasks of an unknown user$")]
    public Task WhenListsTheTasksOfAnUnknownUser(string persona) =>
        ListAsync(persona, $"/api/v1/users/{Guid.CreateVersion7()}/tasks", filter: null, sort: null);

    // ---- Assertions ----

    [Then(@"^the listed tasks are ""([^""]*)""$")]
    public void ThenTheListedTasksAre(string expected)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal(SplitList(expected), ListedTitles());
    }

    [Then(@"^no tasks are listed$")]
    public void ThenNoTasksAreListed()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Empty(ListedTitles());
    }

    [Then(@"^the total count is (\d+)$")]
    public void ThenTheTotalCountIs(int expected)
    {
        Assert.Equal(expected, world.Response.TotalCount);
    }

    [Then(@"^the task ""([^""]+)"" is not among the listed tasks$")]
    public void ThenTheTaskIsNotListed(string title)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.DoesNotContain(title, ListedTitles());
    }

    [Then(@"^the tasks are listed in creation order$")]
    public void ThenTheTasksAreListedInCreationOrder()
    {
        List<Guid> listed = Entries().Select(e => e.GetProperty("id").GetGuid()).ToList();
        List<Guid> expected = world.TaskCreationOrder.Where(listed.Contains).ToList();
        Assert.Equal(expected, listed);
    }

    [Then(@"^the tasks with a due date are listed in the order ""([^""]+)""$")]
    public void ThenTasksWithDueDateAreListedInOrder(string expected)
    {
        List<string> withDueDate = Entries()
            .Where(e => e.GetProperty("dueDate").ValueKind != JsonValueKind.Null)
            .Select(e => e.GetProperty("title").GetString()!)
            .ToList();
        Assert.Equal(SplitList(expected), withDueDate);
    }

    [Then(@"^the first listed task is ""([^""]+)""$")]
    public void ThenTheFirstListedTaskIs(string title)
    {
        Assert.Equal(title, ListedTitles().FirstOrDefault());
    }

    [Then(@"^both reads return the same tasks on the same pages$")]
    public void ThenBothReadsAreIdentical()
    {
        Assert.Equal(2, _repeatedReads.Count);
        Assert.Equal(_repeatedReads[0], _repeatedReads[1]);

        // And the two pages together list every task exactly once.
        List<string> titles = _repeatedReads[0].Where(t => !t.StartsWith("---", StringComparison.Ordinal)).ToList();
        Assert.Equal(titles.Count, titles.Distinct().Count());
    }

    /// <summary>
    /// A two-column table without a header row: each line is "field | expected value".
    /// Reqnroll treats the first line as the header, so it is checked too.
    /// </summary>
    [Then(@"^the entry for ""([^""]+)"" shows:$")]
    public void ThenTheEntryShows(string title, DataTable table)
    {
        JsonElement entry = Entries().Single(e => e.GetProperty("title").GetString() == title);

        IEnumerable<(string Field, string Value)> expectations =
            new[] { (table.Header.ElementAt(0), table.Header.ElementAt(1)) }
                .Concat(table.Rows.Select(r => (r[0], r[1])));

        foreach ((string field, string value) in expectations)
        {
            string actual = field.Trim() switch
            {
                "status" => entry.GetProperty("status").GetString() ?? string.Empty,
                "priority" => entry.GetProperty("priority").GetString() ?? string.Empty,
                "assignee name" => entry.GetProperty("assigneeDisplayName").GetString() ?? string.Empty,
                "comment count" => entry.GetProperty("commentCount").GetInt32().ToString(CultureInfo.InvariantCulture),
                "attachment count" => entry.GetProperty("attachmentCount").GetInt32().ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException($"Unknown list-entry field '{field}'."),
            };

            Assert.True(value.Trim() == actual, $"Expected {field} '{value.Trim()}' for '{title}', got '{actual}'.");
        }
    }

    // ---- Helpers ----

    private async Task ListAsync(string persona, string path, string? filter, (string Field, string Direction)? sort,
        int? page = null, int? pageSize = null)
    {
        var query = new List<string>();

        if (filter is not null)
        {
            query.AddRange(FilterParameters(filter));
        }

        if (sort is (string field, string direction))
        {
            query.Add($"sortBy={Uri.EscapeDataString(field)}");
            query.Add($"sortDescending={(direction == "descending" ? "true" : "false")}");
        }

        if (page is int p)
        {
            query.Add($"page={p}");
        }

        if (pageSize is int size)
        {
            query.Add($"pageSize={size}");
        }

        string url = query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
        await api.SendAsync(persona, HttpMethod.Get, url);
    }

    /// <summary>
    /// Translates filter phrases, joined by " and ": <c>board "X"</c>, <c>assignee Bob</c>,
    /// <c>status InReview</c> (or a quoted raw value), <c>priority High</c>,
    /// <c>due before N days</c>, <c>due after N days</c>.
    /// </summary>
    private IEnumerable<string> FilterParameters(string filter)
    {
        foreach (string clause in filter.Split(" and ", StringSplitOptions.TrimEntries))
        {
            string[] words = clause.Split(' ', 2);
            string argument = words.Length > 1 ? words[1].Trim('"') : string.Empty;

            yield return words[0] switch
            {
                "board" => $"boardId={world.BoardNamed(argument)}",
                "assignee" => $"assigneeId={world.PersonaNamed(argument).UserId}",
                "status" => $"status={Uri.EscapeDataString(argument)}",
                "priority" => $"priority={Uri.EscapeDataString(argument)}",
                "due" => DueParameter(argument),
                _ => throw new InvalidOperationException($"Unknown filter phrase '{clause}'."),
            };
        }
    }

    private static string DueParameter(string argument)
    {
        // "before 6 days" / "after 6 days"
        string[] parts = argument.Split(' ');
        int days = int.Parse(parts[1], CultureInfo.InvariantCulture);
        string when = Uri.EscapeDataString(TestHost.Clock.GetUtcNow().AddDays(days).ToString("O", CultureInfo.InvariantCulture));

        return parts[0] switch
        {
            "before" => $"dueBefore={when}",
            "after" => $"dueAfter={when}",
            _ => throw new InvalidOperationException($"Unknown due-date filter '{argument}'."),
        };
    }

    private List<JsonElement> Entries() => world.Response.Json.EnumerateArray().ToList();

    private List<string> ListedTitles() =>
        Entries().Select(e => e.GetProperty("title").GetString()!).ToList();

    private static List<string> SplitList(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
