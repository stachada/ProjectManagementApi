using System.Net;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;
using Serilog.Events;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// The API as an operator sees it: the environment it runs in, its database being down, its
/// health endpoint, and what it writes to the logs.
/// </summary>
[Binding]
internal sealed class HostSteps(World world, ApiDriver api)
{
    // ---- Environment and failures ----

    [Given(@"^the API runs in the (Development|Production) environment$")]
    public void GivenTheApiRunsIn(string environment) =>
        world.Host = environment == "Development" ? TestHost.Development : TestHost.Production;

    [Given(@"^the database is unavailable$")]
    public void GivenTheDatabaseIsUnavailable() =>
        TestHost.Outage.IsActive = true;

    // ---- Health and the API description ----

    [When(@"^the health check is called (?:anonymously|without an access token)$")]
    public Task WhenTheHealthCheckIsCalled() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Get, "/health");

    [When(@"^the OpenAPI document is requested anonymously$")]
    public Task WhenTheOpenApiDocumentIsRequested() =>
        api.SendAsync(ApiDriver.Anonymous, HttpMethod.Get, "/openapi/v1.json");

    [When(@"^(\w+) requests the OpenAPI document$")]
    public Task WhenPersonaRequestsTheOpenApiDocument(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, "/openapi/v1.json");

    [Then(@"^the health status is ""(\w+)""$")]
    public void ThenTheHealthStatusIs(string status) =>
        Assert.Equal(status, world.Response.Body.Trim());

    [Then(@"^the response signals service unavailable$")]
    public void ThenTheResponseSignalsServiceUnavailable() =>
        Assert.True(world.Response.StatusCode == HttpStatusCode.ServiceUnavailable, world.Response.ToString());

    /// <summary>
    /// The 500 says nothing about how the API is built: no exception type or stack frame, no SQL,
    /// and none of the connection details the underlying failure carried.
    /// </summary>
    [Then(@"^the response contains no stack trace, SQL or connection details$")]
    public void ThenTheResponseLeaksNothing()
    {
        string body = world.Response.Body;
        string[] leaks =
        [
            "Exception", "   at ", ".cs:line", "SELECT ", "FROM [", "Server=", "Password", "ordinis-db", DatabaseOutage.ConnectionDetails,
        ];

        foreach (string leak in leaks)
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Logs ----

    [Then(@"^a request log entry records the method, path, status code, duration and correlation id ""([^""]+)""$")]
    public void ThenARequestLogEntryRecordsTheRequest(string correlationId)
    {
        LogEvent entry = RequestLogEntry(correlationId);

        Assert.Equal(world.Response.RequestPath, Scalar(entry, "Path"));
        Assert.Equal("GET", Scalar(entry, "Method"));
        Assert.Equal(((int)world.Response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), Scalar(entry, "StatusCode"));
        Assert.True(entry.Properties.ContainsKey("ElapsedMilliseconds"), $"No duration in: {entry.RenderMessage()}");
    }

    /// <summary>
    /// The request is identified by ASP.NET Core's per-request <c>RequestId</c> scope property, taken
    /// from the request's own log line; every event carrying that RequestId must also carry the
    /// correlation id.
    /// </summary>
    [Then(@"^every log entry written while handling the request has CorrelationId ""([^""]+)""$")]
    public void ThenEveryLogEntryOfTheRequestHasTheCorrelationId(string correlationId)
    {
        string requestId = Scalar(RequestLogEntry(correlationId), "RequestId")
            ?? throw new InvalidOperationException("The request log entry has no RequestId to group the request's entries by.");

        List<LogEvent> entries = TestHost.Logs.Events.Where(e => Scalar(e, "RequestId") == requestId).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(Scalar(e, "CorrelationId") == correlationId,
            $"Entry without the correlation id: [{e.Level}] {e.RenderMessage()}"));
    }

    private static LogEvent RequestLogEntry(string correlationId) =>
        TestHost.Logs.Events.SingleOrDefault(e =>
            e.MessageTemplate.Text.StartsWith("HTTP {Method}", StringComparison.Ordinal) && Scalar(e, "CorrelationId") == correlationId)
        ?? throw new InvalidOperationException(
            $"No request log entry with correlation id '{correlationId}' among {TestHost.Logs.Events.Count} captured events.");

    private static string? Scalar(LogEvent entry, string property) =>
        entry.Properties.TryGetValue(property, out LogEventPropertyValue? value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;
}
