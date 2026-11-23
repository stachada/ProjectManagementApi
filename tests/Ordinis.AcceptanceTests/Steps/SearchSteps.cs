using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>Searching tasks and projects through <c>/api/v1/search</c>, and what was found.</summary>
[Binding]
internal sealed class SearchSteps(World world, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>A resource type as written in the feature: bare (<c>tasks</c>) or quoted (<c>"TASKS"</c>).</summary>
    private const string Type = @"(\w+|""\w+"")";

    // ---- Actions ----

    [When("^" + Actor + " searches " + Type + @" for ""([^""]+)""$")]
    public Task WhenPersonaSearches(string persona, string type, string term) =>
        SearchAsync(persona, type, $"q={Uri.EscapeDataString(term)}&pageSize=100");

    [When("^" + Actor + " searches " + Type + @" without a term$")]
    public Task WhenPersonaSearchesWithoutATerm(string persona, string type) =>
        SearchAsync(persona, type, "pageSize=100");

    [When("^" + Actor + " searches " + Type + @" for ""([^""]+)"", page (\d+) with (\d+) results per page$")]
    public Task WhenPersonaSearchesAPage(string persona, string type, string term, int page, int pageSize) =>
        SearchAsync(persona, type, $"q={Uri.EscapeDataString(term)}&page={page}&pageSize={pageSize}");

    [When("^" + Actor + " searches " + Type + @" for ""([^""]+)"" returning only ""([^""]+)""$")]
    public Task WhenPersonaSearchesSelectedFields(string persona, string type, string term, string fields) =>
        SearchAsync(persona, type, $"q={Uri.EscapeDataString(term)}&fields={Uri.EscapeDataString(fields)}&pageSize=100");

    // ---- Assertions ----

    [Then(@"^the found tasks are ""([^""]+)""$")]
    public void ThenTheFoundTasksAre(string expected) =>
        Assert.Equal(Split(expected), Found("title"));

    [Then(@"^the found projects are ""([^""]+)""$")]
    public void ThenTheFoundProjectsAre(string expected) =>
        Assert.Equal(Split(expected), Found("name"));

    [Then(@"^(?:nothing is|no (?:tasks|projects) are) found$")]
    public void ThenNothingIsFound() =>
        Assert.Empty(Results());

    [Then(@"^(\d+) tasks are found$")]
    public void ThenTasksAreFound(int count) =>
        Assert.Equal(count, Results().Count);

    [Then(@"^every result contains only id, title and status$")]
    public void ThenEveryResultContainsOnlyTheSelectedFields()
    {
        List<JsonElement> results = Results();
        Assert.NotEmpty(results);

        foreach (JsonElement result in results)
        {
            Assert.Equal(
                new[] { "id", "status", "title" },
                result.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).Order());
        }
    }

    // ---- Helpers ----

    private Task SearchAsync(string persona, string type, string query) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/search?type={Uri.EscapeDataString(type.Trim('"'))}&{query}");

    private List<JsonElement> Results()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal(JsonValueKind.Array, world.Response.Json.ValueKind);
        return world.Response.Json.EnumerateArray().ToList();
    }

    private List<string> Found(string property) =>
        Results().Select(r => r.GetProperty(property).GetString()!).ToList();

    private static List<string> Split(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
