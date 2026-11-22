using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>Audit-trail and webhook-delivery assertions — the side effects of task events.</summary>
[Binding]
internal sealed class WebhookSteps(World world, Seeder seeder, ApiDriver api)
{
    /// <summary>The receiver used when a scenario does not name one.</summary>
    public const string DefaultReceiver = "default";

    /// <summary>Upper bound for asynchronous delivery: outbox poll (5 s) + delivery poll (1 s) + slack.</summary>
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    [Then(@"^the audit trail of project ""([^""]+)"" shows a ""(\w+)"" entry by (\w+) for ""([^""]+)""$")]
    public async Task ThenTheAuditTrailShowsAnEntry(string project, string eventType, string actor, string title)
    {
        Guid projectId = world.ProjectNamed(project);
        Guid observerId = await seeder.ObserverForOrganizationAsync(world.ProjectOrganizations[projectId]);
        ApiResponse audit = await api.SendAsUserAsync(observerId, HttpMethod.Get, $"/api/v1/projects/{projectId}/audit?pageSize=100");
        Assert.True(audit.IsSuccess, $"Could not read the audit trail: {audit}");

        Guid actorId = world.PersonaNamed(actor).UserId;
        Guid taskId = world.TaskNamed(title);

        bool found = audit.Json.EnumerateArray().Any(entry =>
            entry.GetProperty("eventType").GetString() == eventType
            && entry.GetProperty("actorId").GetGuid() == actorId
            && entry.GetProperty("payload").TryGetProperty("TaskId", out JsonElement id)
            && id.GetGuid() == taskId);

        Assert.True(found, $"No '{eventType}' entry by {actor} for '{title}' in: {audit.Body}");
    }

    [Then(@"^the webhook eventually receives a ""([^""]+)"" event for ""([^""]+)""$")]
    public async Task ThenTheWebhookEventuallyReceives(string eventType, string title)
    {
        Guid taskId = world.TaskNamed(title);

        ReceivedWebhook? delivery = await TestHost.Receiver.WaitForAsync(
            DefaultReceiver,
            w => w.EventName == eventType && w.TaskId == taskId,
            DeliveryTimeout);

        Assert.True(
            delivery is not null,
            $"No '{eventType}' delivery for '{title}' within {DeliveryTimeout.TotalSeconds:0} s. " +
            $"Received: [{string.Join(", ", TestHost.Receiver.ReceivedBy(DefaultReceiver).Select(r => r.EventName))}]");
        Assert.StartsWith("application/json", delivery.ContentType);
    }
}
