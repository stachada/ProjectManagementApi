using System.Reflection;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Application.Tasks.Dtos;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// What a response looks like rather than what it says: selected fields (sparse fieldsets),
/// caching headers, and how the API versions of one resource relate.
/// </summary>
[Binding]
internal sealed class RepresentationSteps(World world, ApiDriver api)
{
    // ---- Sparse fieldsets ----

    [Then(@"^every entry contains exactly the fields ""([^""]+)""$")]
    public void ThenEveryEntryContainsExactlyTheFields(string fields)
    {
        string[] expected = fields.Split(',', StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToArray();

        foreach (JsonElement entry in Entries())
        {
            Assert.Equal(expected, entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
    }

    [Then(@"^every entry contains all task summary fields$")]
    public void ThenEveryEntryContainsAllTaskSummaryFields()
    {
        string[] expected = typeof(TaskSummaryDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (JsonElement entry in Entries())
        {
            Assert.Equal(expected, entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        }
    }

    // ---- Caching ----

    [Given(@"^(\w+) has viewed the task ""([^""]+)""$")]
    public async Task GivenPersonaHasViewedTheTask(string persona, string title)
    {
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{world.TaskNamed(title)}");
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not view the task: {response}");
    }

    [Then(@"^the response may be cached by the client only, for (\d+) seconds$")]
    public void ThenTheResponseMayBeCachedPrivately(int seconds)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        string[] directives = CacheControl();

        Assert.Contains("private", directives);
        Assert.Contains($"max-age={seconds}", directives);
        Assert.DoesNotContain("public", directives);
    }

    [Then(@"^the response varies by caller and encoding$")]
    public void ThenTheResponseVariesByCallerAndEncoding()
    {
        string[] vary = (world.Response.Header("Vary") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(vary, v => v.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vary, v => v.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>No caching permission: no <c>max-age</c> above zero, and neither <c>public</c> nor <c>private</c>.</summary>
    [Then(@"^the response does not allow caching$")]
    public void ThenTheResponseDoesNotAllowCaching()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        string[] directives = CacheControl();

        Assert.DoesNotContain(directives, d => d is "public" or "private");
        Assert.DoesNotContain(directives, d => d.StartsWith("max-age=", StringComparison.Ordinal) && d != "max-age=0");
    }

    // ---- API versions ----

    [Then(@"^both responses describe the same task with the same version$")]
    public void ThenBothResponsesDescribeTheSameTask()
    {
        (JsonElement v1, JsonElement v2) = BothVersions();
        Assert.Equal(v1.GetProperty("id").GetGuid(), v2.GetProperty("id").GetGuid());
        Assert.Equal(world.PreviousResponse!.ETag, world.Response.ETag);
        Assert.Equal(v1.GetProperty("concurrencyToken").GetString(), v2.GetProperty("concurrencyToken").GetString());
    }

    /// <summary>Every v1 field is in v2 with the same value — links excepted, which the next step compares.</summary>
    [Then(@"^the version 2 response has every field of the version 1 response$")]
    public void ThenVersion2HasEveryVersion1Field()
    {
        (JsonElement v1, JsonElement v2) = BothVersions();

        foreach (JsonProperty field in v1.EnumerateObject().Where(f => f.Name != "_links"))
        {
            Assert.True(v2.TryGetProperty(field.Name, out JsonElement value), $"Version 2 has no '{field.Name}'.");
            Assert.Equal(field.Value.GetRawText(), value.GetRawText());
        }
    }

    [Then(@"^the version 2 response additionally links to board ""([^""]+)""$")]
    public void ThenVersion2AdditionallyLinksToTheBoard(string board)
    {
        (JsonElement v1, JsonElement v2) = BothVersions();
        string boardHref = $"/api/v1/boards/{world.BoardNamed(board)}";

        Assert.Contains(v2.GetProperty("_links").EnumerateArray(), l => l.GetProperty("rel").GetString() == "board"
            && l.GetProperty("href").GetString() == boardHref);
        Assert.DoesNotContain(v1.GetProperty("_links").EnumerateArray(), l => l.GetProperty("rel").GetString() == "board");
    }

    [When(@"^someone calls (sign-in|refresh|sign-out|health check)$")]
    public Task WhenSomeoneCallsAnUnversionedEndpoint(string endpoint) => endpoint switch
    {
        "sign-in" => api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/login", new { }),
        "refresh" => api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/refresh", new { }),
        "sign-out" => api.SendAsync(ApiDriver.Anonymous, HttpMethod.Post, "/auth/logout"),
        _ => api.SendAsync(ApiDriver.Anonymous, HttpMethod.Get, "/health"),
    };

    /// <summary>The route is mapped: whatever the outcome, it is not "no such address" (404 / 405).</summary>
    [Then(@"^the endpoint exists$")]
    public void ThenTheEndpointExists()
    {
        Assert.True((int)world.Response.StatusCode is not (404 or 405), $"Expected the endpoint to exist, got {world.Response}");
    }

    // ---- Helpers ----

    private List<JsonElement> Entries()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        List<JsonElement> entries = world.Response.Json.EnumerateArray().ToList();
        Assert.NotEmpty(entries);
        return entries;
    }

    private string[] CacheControl() =>
        (world.Response.Header("Cache-Control") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.ToLowerInvariant())
            .ToArray();

    /// <summary>The previous response (version 1) and the latest (version 2) of one task.</summary>
    private (JsonElement V1, JsonElement V2) BothVersions()
    {
        ApiResponse v1 = world.PreviousResponse ?? throw new InvalidOperationException("Only one response was recorded.");
        Assert.True(v1.IsSuccess && world.Response.IsSuccess, $"{v1} | {world.Response}");
        return (v1.Json, world.Response.Json);
    }
}
