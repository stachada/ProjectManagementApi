using System.Net;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Projects;
using Ordinis.Infrastructure.Webhooks;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Webhooks: registering and unregistering them, the audit trail, and delivery to the scripted
/// receivers of <see cref="WebhookReceiver"/>.
/// </summary>
/// <remarks>
/// Before the first action ("When") of a webhook scenario, the event pipeline is drained and the
/// receivers forget what they got: the arrangement's own events (a seeded task is a
/// <c>task.created</c>) are history, and only what the action causes is observed.
/// <para>
/// Retries keep their production schedule (30 s, then 60 s). Steps that wait for a delivery to
/// play out skip the <see cref="TestHost.Clock"/> ahead to each retry as soon as it is scheduled,
/// as if that time had simply passed; the retry timing steps move it deliberately.
/// </para>
/// </remarks>
[Binding]
internal sealed class WebhookSteps(World world, Seeder seeder, ApiDriver api, ScenarioContext scenario)
{
    public const string DefaultReceiver = "default";

    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    /// <summary>Every event type a webhook can subscribe to.</summary>
    private static readonly string[] AllEventTypes = ["task.created", "task.moved", "task.assigned", "comment.added"];

    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How far a retry may be off its schedule ("about 30 seconds later").</summary>
    private static readonly TimeSpan RetryTolerance = TimeSpan.FromSeconds(3);

    /// <summary>Long enough for the delivery job (polling every 250 ms here) to act if it were going to.</summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1.5);

    private bool _drained;
    private Guid? _projectWithSeededWebhooks;
    private ReceivedWebhook? _lastReceived;

    [BeforeStep("area-webhooks")]
    public async Task DrainBeforeTheFirstAction()
    {
        if (_drained || scenario.StepContext.StepInfo.StepDefinitionType != Reqnroll.Bindings.StepDefinitionType.When)
        {
            return;
        }

        _drained = true;
        if (TestHost.WorkersRunning)
        {
            await WaitUntilSettledAsync();
        }

        TestHost.Receiver.ForgetReceived();
    }

    // ---- Audit trail ----

    [Then(@"^the audit trail of project ""([^""]+)"" shows a ""(\w+)"" entry by (\w+) for ""([^""]+)""$")]
    public async Task ThenTheAuditTrailShowsAnEntry(string project, string eventType, string actor, string title)
    {
        JsonElement audit = await AuditTrailAsync(project);
        Guid actorId = world.PersonaNamed(actor).UserId;
        Guid taskId = world.TaskNamed(title);

        bool found = audit.EnumerateArray().Any(entry =>
            entry.GetProperty("eventType").GetString() == eventType
            && entry.GetProperty("actorId").GetGuid() == actorId
            && entry.GetProperty("payload").TryGetProperty("TaskId", out JsonElement id)
            && id.GetGuid() == taskId);

        Assert.True(found, $"No '{eventType}' entry by {actor} for '{title}' in: {audit}");
    }

    [Then(@"^the audit trail of project ""([^""]+)"" shows, newest first:$")]
    public async Task ThenTheAuditTrailShowsNewestFirst(string project, DataTable expected)
    {
        List<JsonElement> newest = (await AuditTrailAsync(project)).EnumerateArray().Take(expected.RowCount).ToList();
        Assert.Equal(expected.RowCount, newest.Count);

        foreach ((DataTableRow row, JsonElement entry) in expected.Rows.Zip(newest))
        {
            Assert.Equal(row["event"], entry.GetProperty("eventType").GetString());
            Assert.Equal(world.PersonaNamed(row["actor"]).UserId, entry.GetProperty("actorId").GetGuid());
            Assert.Equal(world.TaskNamed(row["task"]), entry.GetProperty("payload").GetProperty("TaskId").GetGuid());
        }
    }

    // ---- Arranging webhooks and hosts ----

    /// <summary>
    /// <c>at receiver "Chat"</c> targets that scripted receiver; <c>at "https://…"</c> targets the
    /// address itself. Seeded through the domain, so the URL guard is not consulted.
    /// </summary>
    [Given(@"^project ""([^""]+)"" has a webhook at (receiver ""[^""]+""|""[^""]+"")( for all events| for ""[^""]+""|)$")]
    public Task GivenProjectHasAWebhook(string project, string target, string events) =>
        seeder.RegisterWebhookAsync(world.ProjectNamed(project), TargetUrl(target), EventTypes(events));

    [Given(@"^project ""([^""]+)"" has (\d+) webhooks$")]
    public async Task GivenProjectHasWebhooks(string project, int count)
    {
        Guid projectId = world.ProjectNamed(project);
        for (int i = 1; i <= count; i++)
        {
            await seeder.RegisterWebhookAsync(projectId, $"https://hooks.example.com/seeded-{i:00}", []);
        }

        _projectWithSeededWebhooks = projectId;
    }

    [Given(@"^(\w+) has unregistered one of them$")]
    public async Task GivenPersonaHasUnregisteredOne(string persona)
    {
        Guid projectId = _projectWithSeededWebhooks ?? throw new InvalidOperationException("No project's webhooks were seeded.");
        Webhook first = (await seeder.WebhooksOfAsync(projectId))[0];
        ApiResponse response = await UnregisterAsync(persona, projectId, first.Id, string.Empty);
        Assert.True(response.IsSuccess, $"Arrangement failed — could not unregister a webhook: {response}");
    }

    [Given(@"^(\w+) has unregistered the webhook at ""([^""]+)"" from project ""([^""]+)""$")]
    public async Task GivenPersonaHasUnregisteredTheWebhook(string persona, string url, string project)
    {
        ApiResponse response = await UnregisterAsync(persona, world.ProjectNamed(project), await WebhookIdAsync(project, url), string.Empty);
        Assert.True(response.IsSuccess, $"Arrangement failed — could not unregister the webhook: {response}");
    }

    [Given(@"^the host ""([^""]+)"" (?:now )?resolves to ([0-9a-fA-F:.]+)$")]
    public void GivenTheHostResolvesTo(string host, string address) =>
        TestHost.Dns.Point(host, IPAddress.Parse(address));

    [Given(@"^a receiver ""([^""]+)"" listening at ""([^""]+)""$")]
    public void GivenAReceiver(string receiver, string address)
    {
        // The address is a label (see WebhookReceiver); receivers listen on loopback.
    }

    [Given(@"^receiver ""([^""]+)"" responds with (\d+) to the first request$")]
    public void GivenReceiverFailsTheFirstRequest(string receiver, int status) =>
        TestHost.Receiver.Script(receiver, attempt => attempt == 1 ? new ReceiverResponse(status) : ReceiverResponse.Ok);

    [Given(@"^receiver ""([^""]+)"" responds with (\d+) to the first (\d+) requests$")]
    public void GivenReceiverFailsTheFirstRequests(string receiver, int status, int count) =>
        TestHost.Receiver.Script(receiver, attempt => attempt <= count ? new ReceiverResponse(status) : ReceiverResponse.Ok);

    [Given(@"^receiver ""([^""]+)"" always responds with (\d+)$")]
    public void GivenReceiverAlwaysFails(string receiver, int status) =>
        TestHost.Receiver.Script(receiver, _ => new ReceiverResponse(status));

    [Given(@"^receiver ""([^""]+)"" never answers$")]
    public void GivenReceiverNeverAnswers(string receiver) =>
        TestHost.Receiver.Script(receiver, _ => new ReceiverResponse(Hang: TimeSpan.FromMinutes(5)));

    [Given(@"^receiver ""([^""]+)"" responds to the first request with (status \d+ redirect|status \d+|no answer within 10 seconds|a refused connection)$")]
    public void GivenReceiverMisbehavesOnce(string receiver, string response)
    {
        ReceiverResponse first = response switch
        {
            "no answer within 10 seconds" => new ReceiverResponse(Hang: TimeSpan.FromSeconds(20)),
            "a refused connection" => new ReceiverResponse(Abort: true),
            _ when response.EndsWith("redirect", StringComparison.Ordinal) =>
                new ReceiverResponse(int.Parse(response.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture), TestHost.Receiver.UrlFor("elsewhere")),
            _ => new ReceiverResponse(int.Parse(response.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)),
        };

        TestHost.Receiver.Script(receiver, attempt => attempt == 1 ? first : ReceiverResponse.Ok);
    }

    [Given(@"^receiver ""([^""]+)"" redirects every request to ""([^""]+)""$")]
    public void GivenReceiverRedirects(string receiver, string target) =>
        TestHost.Receiver.Script(receiver, _ => new ReceiverResponse(302, target));

    [Given(@"^the background workers are stopped$")]
    public Task GivenTheWorkersAreStopped() => TestHost.StopWorkersAsync();

    [When(@"^the background workers are started again$")]
    public Task WhenTheWorkersAreStartedAgain() => TestHost.StartWorkersAsync();

    [Given(@"^two API instances share the database$")]
    public void GivenTwoInstancesShareTheDatabase() => TestHost.StartSecondInstance();

    // ---- Registering and unregistering ----

    [When("^" + Actor + @" registers a webhook for project ""([^""]+)""$")]
    public async Task WhenPersonaRegistersAWebhook(string persona, string project)
    {
        Guid projectId = world.ProjectNamed(project);
        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/projects/{projectId}/webhooks",
            new { url = TestHost.Receiver.UrlFor(DefaultReceiver), eventTypes = (string[]?)null },
            await seeder.CurrentProjectVersionAsync(projectId));
    }

    [When("^" + Actor + @" registers a webhook for project ""([^""]+)"" at ""([^""]*)""( for ""[^""]+""|)" + VersionSuffix.Pattern + "$")]
    public async Task WhenPersonaRegistersAWebhookAt(string persona, string project, string url, string events, string suffix)
    {
        Guid projectId = world.ProjectNamed(project);
        string? ifMatch = await VersionSuffix.ResolveAsync(suffix, world, projectId, () => seeder.CurrentProjectVersionAsync(projectId));
        string[]? eventTypes = events.Length == 0 ? null : EventTypes(events);

        await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/projects/{projectId}/webhooks", new { url, eventTypes }, ifMatch);
    }

    [When("^" + Actor + @" unregisters the webhook at ""([^""]+)"" from project ""([^""]+)""" + VersionSuffix.Pattern + "$")]
    public async Task WhenPersonaUnregistersTheWebhook(string persona, string url, string project, string suffix) =>
        await UnregisterAsync(persona, world.ProjectNamed(project), await WebhookIdAsync(project, url), suffix);

    [When("^" + Actor + @" unregisters an unknown webhook from project ""([^""]+)""$")]
    public Task WhenPersonaUnregistersAnUnknownWebhook(string persona, string project) =>
        UnregisterAsync(persona, world.ProjectNamed(project), Guid.CreateVersion7(), string.Empty);

    [When(@"^(\w+) creates (\d+) tasks on board ""([^""]+)""$")]
    public async Task WhenPersonaCreatesTasks(string persona, int count, string board)
    {
        Guid boardId = world.BoardNamed(board);
        for (int i = 1; i <= count; i++)
        {
            ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, "/api/v1/tasks", new { boardId, title = $"Batch task {i:00}" });
            Assert.True(response.IsSuccess, response.ToString());
        }
    }

    // ---- Webhook registrations ----

    [Then(@"^the response points to the webhooks of project ""([^""]+)""$")]
    public void ThenTheResponsePointsToTheWebhooks(string project)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        Assert.Equal($"/api/v1/projects/{world.ProjectNamed(project)}/webhooks", world.Response.Location?.OriginalString);
    }

    [Then(@"^the webhooks of project ""([^""]+)"" are:$")]
    public async Task ThenTheWebhooksAre(string project, DataTable expected)
    {
        List<JsonElement> webhooks = (await WebhookListAsync(project)).EnumerateArray().ToList();
        Assert.Equal(expected.RowCount, webhooks.Count);

        foreach ((DataTableRow row, JsonElement webhook) in expected.Rows.Zip(webhooks))
        {
            Assert.Equal(row["url"], webhook.GetProperty("url").GetString());
            Assert.Equal(EventTypes($@" for ""{row["events"]}"""), webhook.GetProperty("eventTypes").EnumerateArray().Select(e => e.GetString()!).ToArray());
            Assert.Equal(world.PersonaNamed(row["registered by"]).UserId, webhook.GetProperty("registeredByUserId").GetGuid());
        }
    }

    /// <summary>"All event types" is an empty filter (or, equivalently, a filter listing every type).</summary>
    [Then(@"^the webhook at ""([^""]+)"" receives all event types$")]
    public async Task ThenTheWebhookReceivesAllEventTypes(string url)
    {
        Guid projectId = world.Projects.Values.Single();
        Webhook webhook = (await seeder.WebhooksOfAsync(projectId)).Single(w => w.Url == url);
        Assert.True(webhook.EventTypes.Count == 0 || AllEventTypes.All(webhook.EventTypes.Contains),
            $"Expected no event filter, got [{string.Join(", ", webhook.EventTypes)}].");
    }

    [Then(@"^project ""([^""]+)"" has no webhooks$")]
    public async Task ThenProjectHasNoWebhooks(string project) =>
        Assert.Empty(await seeder.WebhooksOfAsync(world.ProjectNamed(project)));

    [Then(@"^the receiver at ""([^""]+)"" receives nothing within the delivery window$")]
    public async Task ThenTheReceiverAtReceivesNothing(string url)
    {
        await WaitUntilSettledAsync();
        Assert.Equal(0, await seeder.CountDeliveriesToAsync(url));
    }

    // ---- What receivers get ----

    /// <summary>The default receiver (the one "has a webhook subscribed to …" registers) gets the event for that task.</summary>
    [Then(@"^the webhook eventually receives a ""([^""]+)"" event for ""([^""]+)""$")]
    public async Task ThenTheWebhookEventuallyReceives(string eventType, string title)
    {
        Guid taskId = world.TaskNamed(title);

        ReceivedWebhook? delivery = await TestHost.Receiver.WaitForAsync(
            DefaultReceiver,
            w => w.EventName == eventType && w.TaskId == taskId,
            DeliveryTimeout,
            SkipToDueRetryAsync);

        Assert.True(
            delivery is not null,
            $"No '{eventType}' delivery for '{title}' within {DeliveryTimeout.TotalSeconds:0} s. " +
            $"Received: [{string.Join(", ", EventsReceivedBy(DefaultReceiver))}]");
        Assert.StartsWith("application/json", delivery.ContentType ?? string.Empty, StringComparison.Ordinal);
    }

    [Then(@"^receiver ""([^""]+)"" eventually receives one POST with content type ""([^""]+)""$")]
    public async Task ThenReceiverReceivesOnePost(string receiver, string contentType)
    {
        await TestHost.Receiver.WaitForAsync(receiver, _ => true, DeliveryTimeout, SkipToDueRetryAsync);
        await WaitUntilSettledAsync();

        ReceivedWebhook delivery = Assert.Single(TestHost.Receiver.ReceivedBy(receiver));
        Assert.Equal("POST", delivery.Method);
        Assert.StartsWith(contentType, delivery.ContentType ?? string.Empty, StringComparison.Ordinal);
        _lastReceived = delivery;
    }

    [Then(@"^the payload has event ""([^""]+)"", the time it occurred, and data about ""([^""]+)""$")]
    public void ThenThePayloadDescribesTheEvent(string eventName, string title)
    {
        JsonElement payload = (_lastReceived ?? throw new InvalidOperationException("Nothing was received yet.")).Json;

        Assert.Equal(eventName, payload.GetProperty("event").GetString());
        Assert.True(payload.GetProperty("occurredAt").TryGetDateTimeOffset(out DateTimeOffset occurredAt), payload.ToString());
        DateTimeOffset now = TestHost.Clock.GetUtcNow();
        Assert.InRange(occurredAt, now.AddMinutes(-5), now.AddMinutes(1));
        Assert.Equal(world.TaskNamed(title), payload.GetProperty("data").GetProperty("TaskId").GetGuid());
    }

    [Then(@"^receiver ""([^""]+)"" eventually receives exactly one event, ""([^""]+)""$")]
    public async Task ThenReceiverReceivesExactlyOneEvent(string receiver, string eventName)
    {
        await TestHost.Receiver.WaitForAsync(receiver, r => r.EventName == eventName, DeliveryTimeout, SkipToDueRetryAsync);
        await WaitUntilSettledAsync();
        Assert.Equal([eventName], EventsReceivedBy(receiver));
    }

    [Then(@"^receiver ""([^""]+)"" eventually receives ""([^""]+)"" and ""([^""]+)""$")]
    public async Task ThenReceiverReceivesBoth(string receiver, string first, string second)
    {
        await TestHost.Receiver.WaitForCountAsync(receiver, _ => true, 2, DeliveryTimeout, SkipToDueRetryAsync);
        await WaitUntilSettledAsync();
        Assert.Equal(new[] { first, second }.Order(), EventsReceivedBy(receiver).Order());
    }

    [Then(@"^receiver ""([^""]+)"" receives nothing within the delivery window$")]
    public async Task ThenReceiverReceivesNothing(string receiver)
    {
        await WaitUntilSettledAsync();
        Assert.Empty(TestHost.Receiver.ReceivedBy(receiver));
    }

    [Then(@"^receivers ""([^""]+)"" and ""([^""]+)"" each eventually receive one ""([^""]+)"" event$")]
    public async Task ThenBothReceiversReceiveOneEvent(string first, string second, string eventName)
    {
        await ThenReceiverReceivesOneEvent(first, eventName);
        await ThenReceiverReceivesOneEvent(second, eventName);
    }

    [Then(@"^receiver ""([^""]+)"" eventually receives one ""([^""]+)"" event$")]
    public async Task ThenReceiverReceivesOneEvent(string receiver, string eventName)
    {
        ReceivedWebhook? delivery = await TestHost.Receiver.WaitForAsync(receiver, r => r.EventName == eventName, DeliveryTimeout, SkipToDueRetryAsync);
        Assert.True(delivery is not null, $"Receiver '{receiver}' got no '{eventName}' within {DeliveryTimeout.TotalSeconds:0} s.");
        Assert.Single(TestHost.Receiver.ReceivedBy(receiver), r => r.EventName == eventName);
    }

    /// <summary>
    /// The fast receiver got its event within less than the delivery timeout of the slow
    /// receiver being called — i.e. without waiting for the slow one to give up.
    /// </summary>
    [Then(@"^receiver ""([^""]+)"" receives one ""([^""]+)"" event while ""([^""]+)"" is still not answering$")]
    public async Task ThenReceiverIsNotHeldBackBy(string receiver, string eventName, string slowReceiver)
    {
        await ThenReceiverReceivesOneEvent(receiver, eventName);
        ReceivedWebhook? slow = await TestHost.Receiver.WaitForAsync(slowReceiver, _ => true, DeliveryTimeout);
        Assert.True(slow is not null, $"Receiver '{slowReceiver}' was never called.");

        ReceivedWebhook fast = TestHost.Receiver.ReceivedBy(receiver).First(r => r.EventName == eventName);
        TimeSpan heldBack = fast.ReceivedAt - slow.ReceivedAt;
        Assert.True(heldBack < TimeSpan.FromSeconds(5),
            $"'{receiver}' got its event {heldBack.TotalSeconds:0.0} s after '{slowReceiver}' was called — it waited for the slow receiver.");
    }

    [Then(@"^receiver ""([^""]+)"" eventually receives (\d+) ""([^""]+)"" events, one per task$")]
    public async Task ThenReceiverReceivesOneEventPerTask(string receiver, int count, string eventName)
    {
        await TestHost.Receiver.WaitForCountAsync(receiver, r => r.EventName == eventName, count, DeliveryTimeout, SkipToDueRetryAsync);
        await WaitUntilSettledAsync();

        List<ReceivedWebhook> events = TestHost.Receiver.ReceivedBy(receiver).Where(r => r.EventName == eventName).ToList();
        Assert.Equal(count, events.Count);
        Assert.Equal(count, events.Select(e => e.TaskId).Distinct().Count());
    }

    [Then(@"^both attempts received by ""([^""]+)"" have byte-identical bodies$")]
    public async Task ThenBothAttemptsHaveIdenticalBodies(string receiver)
    {
        IReadOnlyList<ReceivedWebhook> attempts = await TestHost.Receiver.WaitForCountAsync(receiver, _ => true, 2, DeliveryTimeout, SkipToDueRetryAsync);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0].Body, attempts[1].Body);
    }

    // ---- Retry timing ----

    [Then(@"^receiver ""([^""]+)"" receives attempt 1 immediately$")]
    public async Task ThenReceiverReceivesTheFirstAttemptImmediately(string receiver)
    {
        // No time is skipped: the first attempt does not wait for any backoff.
        ReceivedWebhook? first = await TestHost.Receiver.WaitForAsync(receiver, _ => true, DeliveryTimeout);
        Assert.True(first is not null, $"Receiver '{receiver}' got no first attempt within {DeliveryTimeout.TotalSeconds:0} s.");
        Assert.Single(TestHost.Receiver.ReceivedBy(receiver));
    }

    /// <summary>
    /// Attempt <paramref name="attempt"/> is not made shortly before <paramref name="seconds"/>
    /// have passed since the previous one, and is made once they have.
    /// </summary>
    [Then(@"^receiver ""([^""]+)"" receives attempt (\d+) about (\d+) seconds (?:later|after that)$")]
    public async Task ThenReceiverReceivesTheNextAttemptAfter(string receiver, int attempt, int seconds)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(seconds);
        ReceivedWebhook previous = TestHost.Receiver.ReceivedBy(receiver)[attempt - 2];

        // The previous attempt's failure must be recorded (and the retry scheduled) before the
        // clock moves; otherwise the retry would be scheduled from the moved clock.
        await WaitForDeliveryAsync(d => d.AttemptCount >= attempt - 1, skipToDueRetries: false);

        TestHost.Clock.AdvanceTo(previous.ReceivedAt + backoff - RetryTolerance);
        await Task.Delay(QuietPeriod);
        Assert.True(TestHost.Receiver.ReceivedBy(receiver).Count == attempt - 1,
            $"Attempt {attempt} came before {seconds} s had passed since attempt {attempt - 1}.");

        TestHost.Clock.AdvanceTo(previous.ReceivedAt + backoff);
        IReadOnlyList<ReceivedWebhook> attempts = await TestHost.Receiver.WaitForCountAsync(receiver, _ => true, attempt, DeliveryTimeout);
        Assert.True(attempts.Count >= attempt, $"Attempt {attempt} was not made once {seconds} s had passed since attempt {attempt - 1}.");

        TimeSpan gap = attempts[attempt - 1].ReceivedAt - previous.ReceivedAt;
        Assert.InRange(gap, backoff - RetryTolerance, backoff + RetryTolerance);
    }

    [Then(@"^receiver ""([^""]+)"" eventually receives exactly (\d+) attempts$")]
    public async Task ThenReceiverReceivesExactlyAttempts(string receiver, int count)
    {
        await TestHost.Receiver.WaitForCountAsync(receiver, _ => true, count, DeliveryTimeout, SkipToDueRetryAsync);
        await WaitForDeliveryAsync(d => d.Status != WebhookDeliveryStatus.Pending);

        // Given up: however much more time passes, no further attempt is made.
        TestHost.Clock.Advance(TimeSpan.FromHours(1));
        await Task.Delay(QuietPeriod);
        Assert.Equal(count, TestHost.Receiver.ReceivedBy(receiver).Count);
    }

    // ---- Delivery records ----

    /// <summary>Delivered by the last attempt; the attempt count records the failed attempts before it.</summary>
    [Then(@"^the delivery is recorded as delivered after (\d+) attempts$")]
    public async Task ThenTheDeliveryIsRecordedAsDeliveredAfter(int attempts)
    {
        WebhookDelivery delivery = await WaitForDeliveryAsync(d => d.Status != WebhookDeliveryStatus.Pending);

        Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(attempts - 1, delivery.AttemptCount);
        Assert.Null(delivery.LastError);
        Assert.Equal(attempts, TestHost.Receiver.ReceivedByAnyone.Count);
    }

    [Then(@"^the delivery is recorded as failed with the last error$")]
    public async Task ThenTheDeliveryIsRecordedAsFailed()
    {
        WebhookDelivery delivery = await WaitForDeliveryAsync(d => d.Status != WebhookDeliveryStatus.Pending);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
        Assert.StartsWith("Received HTTP 500", delivery.LastError, StringComparison.Ordinal);
    }

    /// <summary>The first attempt failed and was recorded, and the delivery was attempted again (and then succeeded).</summary>
    [Then(@"^the delivery is retried$")]
    public async Task ThenTheDeliveryIsRetried()
    {
        WebhookDelivery delivery = await WaitForDeliveryAsync(d => d.Status != WebhookDeliveryStatus.Pending);

        Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(2, TestHost.Receiver.ReceivedByAnyone.Count(r => r.Receiver != "elsewhere"));
    }

    [Then(@"^the delivery attempt is recorded as failed( because the destination is not allowed|)$")]
    public async Task ThenTheAttemptIsRecordedAsFailed(string reason)
    {
        WebhookDelivery delivery = await WaitForDeliveryAsync(d => d.AttemptCount >= 1);

        Assert.NotEqual(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.False(string.IsNullOrEmpty(delivery.LastError));
        if (reason.Length > 0)
        {
            Assert.StartsWith("Blocked", delivery.LastError, StringComparison.Ordinal);
        }
    }

    /// <summary>The redirect was not followed: the attempt ended at the 3xx answer itself.</summary>
    [Then(@"^no request is sent to ""([^""]+)""$")]
    public async Task ThenNoRequestIsSentToTheRedirectTarget(string target)
    {
        WebhookDelivery delivery = await WaitForDeliveryAsync(d => d.AttemptCount >= 1);
        Assert.StartsWith("Received HTTP 302", delivery.LastError, StringComparison.Ordinal);
    }

    /// <summary>Nothing reached any receiver: the blocked delivery never connected anywhere.</summary>
    [Then(@"^no request is sent to ([0-9a-fA-F:.]+)$")]
    public async Task ThenNoRequestIsSentToTheAddress(string address)
    {
        await WaitForDeliveryAsync(d => d.AttemptCount >= 1);
        Assert.Empty(TestHost.Receiver.ReceivedByAnyone);
    }

    // ---- Helpers ----

    private static string TargetUrl(string target) =>
        target.StartsWith("receiver ", StringComparison.Ordinal)
            ? TestHost.Receiver.UrlFor(target.Split('"')[1])
            : target.Trim('"');

    /// <summary>"" or " for all events" → none (all); " for "a, b"" → [a, b].</summary>
    private static string[] EventTypes(string events) =>
        events.Contains('"')
            ? events.Split('"')[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];

    private List<string> EventsReceivedBy(string receiver) =>
        TestHost.Receiver.ReceivedBy(receiver).Select(r => r.EventName ?? "?").ToList();

    private async Task<ApiResponse> UnregisterAsync(string persona, Guid projectId, Guid webhookId, string suffix)
    {
        string? ifMatch = await VersionSuffix.ResolveAsync(suffix, world, projectId, () => seeder.CurrentProjectVersionAsync(projectId));
        return await api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/projects/{projectId}/webhooks/{webhookId}", body: null, ifMatch);
    }

    private async Task<Guid> WebhookIdAsync(string project, string url) =>
        (await seeder.WebhooksOfAsync(world.ProjectNamed(project))).Single(w => w.Url == url).Id;

    private async Task<JsonElement> WebhookListAsync(string project)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid observerId = await seeder.ObserverForOrganizationAsync(world.ProjectOrganizations[projectId]);
        ApiResponse list = await api.SendAsUserAsync(observerId, HttpMethod.Get, $"/api/v1/projects/{projectId}/webhooks");
        Assert.True(list.IsSuccess, $"Could not list the webhooks: {list}");
        return list.Json;
    }

    private async Task<JsonElement> AuditTrailAsync(string project)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid observerId = await seeder.ObserverForOrganizationAsync(world.ProjectOrganizations[projectId]);
        ApiResponse audit = await api.SendAsUserAsync(observerId, HttpMethod.Get, $"/api/v1/projects/{projectId}/audit?pageSize=100");
        Assert.True(audit.IsSuccess, $"Could not read the audit trail: {audit}");
        return audit.Json;
    }

    /// <summary>Waits until every event has been dispatched and every delivery settled (delivered or given up).</summary>
    private async Task WaitUntilSettledAsync()
    {
        DateTime deadline = DateTime.UtcNow + DeliveryTimeout;
        while (await seeder.UnsettledEventWorkAsync() > 0)
        {
            Assert.True(DateTime.UtcNow < deadline, $"The event pipeline did not settle within {DeliveryTimeout.TotalSeconds:0} s.");
            await SkipToDueRetryAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>Lets the time until the earliest scheduled retry pass, so that retry is made now.</summary>
    private async Task SkipToDueRetryAsync()
    {
        if (await seeder.NextRetryDueAsync() is DateTimeOffset due)
        {
            TestHost.Clock.AdvanceTo(due);
        }
    }

    private async Task<WebhookDelivery> WaitForDeliveryAsync(Func<WebhookDelivery, bool> condition, bool skipToDueRetries = true)
    {
        DateTime deadline = DateTime.UtcNow + DeliveryTimeout;
        while (true)
        {
            WebhookDelivery? delivery = await seeder.LatestDeliveryAsync();
            if (delivery is not null && condition(delivery))
            {
                return delivery;
            }

            Assert.True(DateTime.UtcNow < deadline,
                $"No delivery reached the expected state within {DeliveryTimeout.TotalSeconds:0} s (latest: {delivery?.Status} after {delivery?.AttemptCount} attempts, {delivery?.LastError}).");
            if (skipToDueRetries)
            {
                await SkipToDueRetryAsync();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
