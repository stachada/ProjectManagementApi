using System.Text.Json;
using Ordinis.Domain.Tasks;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Domain-event JSON (outbox payloads, which the audit trail and webhooks expose) writes enums by
/// name, and still reads outbox rows written before that, when enums were stored as numbers.
/// </summary>
public sealed class EventJsonTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OutboxMessageFrom_WritesEnumsByName()
    {
        var moved = new TaskMoved(Guid.CreateVersion7(), ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, Guid.CreateVersion7(), Now);

        OutboxMessage message = OutboxMessage.From(moved);

        using JsonDocument payload = JsonDocument.Parse(message.Payload);
        Assert.Equal("ToDo", payload.RootElement.GetProperty("PreviousStatus").GetString());
        Assert.Equal("InProgress", payload.RootElement.GetProperty("NewStatus").GetString());
    }

    [Fact]
    public void Deserialize_PayloadWrittenWithNumericEnums_StillReads()
    {
        Guid taskId = Guid.CreateVersion7();
        string legacyPayload =
            $$"""{"TaskId":"{{taskId}}","PreviousStatus":1,"NewStatus":2,"MovedByUserId":"{{Guid.CreateVersion7()}}","OccurredAt":"2026-11-23T09:00:00+00:00"}""";

        TaskMoved? moved = JsonSerializer.Deserialize<TaskMoved>(legacyPayload, EventJson.Options);

        Assert.NotNull(moved);
        Assert.Equal(taskId, moved.TaskId);
        Assert.Equal(ProjectTaskStatus.ToDo, moved.PreviousStatus);
        Assert.Equal(ProjectTaskStatus.InProgress, moved.NewStatus);
    }

    [Fact]
    public void RoundTrip_ThroughOutboxMessage_PreservesTheEvent()
    {
        var moved = new TaskMoved(Guid.CreateVersion7(), ProjectTaskStatus.InReview, ProjectTaskStatus.Done, Guid.CreateVersion7(), Now);

        TaskMoved? read = JsonSerializer.Deserialize<TaskMoved>(OutboxMessage.From(moved).Payload, EventJson.Options);

        Assert.NotNull(read);
        Assert.Equal(moved.PreviousStatus, read.PreviousStatus);
        Assert.Equal(moved.NewStatus, read.NewStatus);
        Assert.Equal(moved.TaskId, read.TaskId);
    }
}
