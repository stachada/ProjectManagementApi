using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>Searching tasks and projects (<c>GET /api/v1/search</c>).</summary>
[Binding]
internal sealed class SearchSteps(World world, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    [When("^" + Actor + @" searches (tasks|projects) for ""([^""]+)""$")]
    public Task WhenPersonaSearches(string persona, string type, string term) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/search?type={type}&q={Uri.EscapeDataString(term)}&pageSize=100");

    [Then(@"^no (tasks|projects) are found$")]
    public void ThenNothingIsFound(string type)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.True(world.Response.Json.ValueKind == JsonValueKind.Array, $"Expected a list of {type}: {world.Response}");
        Assert.Empty(world.Response.Json.EnumerateArray());
    }
}
