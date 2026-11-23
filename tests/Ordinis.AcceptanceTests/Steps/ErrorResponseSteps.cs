using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// The shape of error responses (RFC 9457 Problem Details) and request correlation: every response
/// carries an <c>X-Correlation-ID</c>, and every Problem Details body repeats it.
/// </summary>
[Binding]
internal sealed class ErrorResponseSteps(World world, ApiDriver api)
{
    private const string CorrelationHeader = "X-Correlation-ID";

    // ---- Requests that fail on purpose ----

    [When(@"^(\w+) creates a task with a body that is not valid JSON$")]
    public Task WhenPersonaSendsInvalidJson(string persona) =>
        api.SendRawBodyAsync(persona, HttpMethod.Post, "/api/v1/tasks", """{ "title": "Write docs", """, "application/json");

    [When(@"^(\w+) patches the task ""([^""]+)""$")]
    public Task WhenPersonaPatchesTheTask(string persona, string title) =>
        api.SendAsync(persona, HttpMethod.Patch, $"/api/v1/tasks/{world.TaskNamed(title)}", new { title = "Patched" });

    [When(@"^(\w+) calls an address that does not exist$")]
    public Task WhenPersonaCallsAnUnknownAddress(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, "/api/v1/no-such-resource");

    [When(@"^(\w+) creates a task on board ""([^""]+)"" with a blank title and an unknown priority$")]
    public Task WhenPersonaCreatesATaskWithTwoInvalidFields(string persona, string board) =>
        api.SendAsync(persona, HttpMethod.Post, "/api/v1/tasks", new { boardId = world.BoardNamed(board), title = "   ", priority = 99 });

    [When(@"^(\w+) views the organization ""([^""]+)"" without a correlation id$")]
    public Task WhenPersonaViewsTheOrganizationWithoutACorrelationId(string persona, string organization) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/organizations/{world.OrganizationNamed(organization)}");

    [When(@"^(\w+) views the organization ""([^""]+)"" twice without a correlation id$")]
    public async Task WhenPersonaViewsTheOrganizationTwice(string persona, string organization)
    {
        await WhenPersonaViewsTheOrganizationWithoutACorrelationId(persona, organization);
        await WhenPersonaViewsTheOrganizationWithoutACorrelationId(persona, organization);
    }

    // ---- Problem Details ----

    [Then(@"^the response is a Problem Details document with status (\d+) and title ""([^""]+)""$")]
    public void ThenTheResponseIsProblemDetails(int status, string title)
    {
        Assert.Equal(status, (int)world.Response.StatusCode);
        Assert.StartsWith("application/problem+json", world.Response.Header("Content-Type") ?? string.Empty, StringComparison.Ordinal);

        JsonElement problem = world.Response.Json;
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("type").GetString()), world.Response.ToString());
    }

    [Then(@"^the Problem Details instance is the request path$")]
    public void ThenTheInstanceIsTheRequestPath()
    {
        Assert.Equal(world.Response.RequestPath, world.Response.Json.GetProperty("instance").GetString());
    }

    [Then(@"^the Problem Details carry the request's correlation id$")]
    public void ThenTheProblemDetailsCarryTheCorrelationId()
    {
        string? header = world.Response.Header(CorrelationHeader);
        Assert.False(string.IsNullOrEmpty(header), $"No {CorrelationHeader} header: {world.Response}");
        Assert.Equal(header, world.Response.Json.GetProperty("correlationId").GetString());
    }

    [Then(@"^the Problem Details type is ""([^""]+)""$")]
    public void ThenTheProblemDetailsTypeIs(string type)
    {
        Assert.Equal(type, world.Response.ProblemType);
    }

    [Then(@"^the validation errors contain messages for the fields:$")]
    public void ThenTheValidationErrorsContainMessagesFor(DataTable fields)
    {
        JsonElement errors = world.Response.Json.GetProperty("errors");

        foreach (DataTableRow row in fields.Rows)
        {
            JsonProperty error = errors.EnumerateObject().SingleOrDefault(e => e.Name.Equals(row["field"], StringComparison.OrdinalIgnoreCase));
            Assert.True(error.Value.ValueKind == JsonValueKind.Array && error.Value.GetArrayLength() > 0,
                $"Expected a message for '{row["field"]}': {world.Response}");
            Assert.All(error.Value.EnumerateArray(), m => Assert.False(string.IsNullOrWhiteSpace(m.GetString())));
        }
    }

    // ---- Correlation ----

    /// <summary>Generated ids are UUIDv7s: unique, and ordered by the time they were made.</summary>
    [Then(@"^the response carries a newly generated, time-ordered correlation id$")]
    public void ThenTheResponseCarriesAGeneratedCorrelationId()
    {
        string? header = world.Response.Header(CorrelationHeader);
        Assert.True(Guid.TryParse(header, out Guid id), $"Expected a generated id in {CorrelationHeader}, got '{header}'.");
        Assert.Equal(7, id.Version);
    }

    [Then(@"^the response carries the correlation id ""([^""]+)""$")]
    public void ThenTheResponseCarriesTheCorrelationId(string correlationId)
    {
        Assert.Equal(correlationId, world.Response.Header(CorrelationHeader));
    }

    [Then(@"^the two responses carry different correlation ids$")]
    public void ThenTheTwoResponsesCarryDifferentCorrelationIds()
    {
        string? first = world.PreviousResponse?.Header(CorrelationHeader);
        string? second = world.Response.Header(CorrelationHeader);

        Assert.False(string.IsNullOrEmpty(first));
        Assert.False(string.IsNullOrEmpty(second));
        Assert.NotEqual(first, second);
    }

    [Then(@"^the Problem Details correlationId is ""([^""]+)""$")]
    public void ThenTheProblemDetailsCorrelationIdIs(string correlationId)
    {
        Assert.Equal(correlationId, world.Response.Json.GetProperty("correlationId").GetString());
    }
}
