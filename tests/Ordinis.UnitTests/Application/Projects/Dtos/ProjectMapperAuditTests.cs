using Ordinis.Application.Projects.Dtos;

namespace Ordinis.UnitTests.Application.Projects.Dtos;

public class ProjectMapperAuditTests
{
    private static readonly Guid ExpectedActorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset OccurredAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("TaskCreated", "ReporterId")]
    [InlineData("TaskMoved", "MovedByUserId")]
    [InlineData("TaskAssigned", "AssignedByUserId")]
    [InlineData("TaskUnassigned", "UnassignedByUserId")]
    [InlineData("CommentAdded", "AuthorId")]
    [InlineData("CommentRemoved", "RemovedByUserId")]
    [InlineData("AttachmentAdded", "UploadedByUserId")]
    [InlineData("AttachmentRemoved", "RemovedByUserId")]
    [InlineData("TaskDeleted", "DeletedByUserId")]
    [InlineData("TaskUpdated", "UpdatedByUserId")]
    public void ToAuditEntryDto_KnownEventType_ExtractsActorIdFromCorrectProperty(
        string eventType, string actorPropertyName)
    {
        // Every real event payload also carries TaskId and other fields, but only the
        // actor property matters for this test - the rest is irrelevant noise here.
        var row = new AuditRow(
            Id: Guid.CreateVersion7(),
            Type: $"Ordinis.Domain.Tasks.{eventType}",
            OccurredAt: OccurredAt,
            Payload: $$"""{"TaskId":"{{Guid.CreateVersion7()}}","{{actorPropertyName}}":"{{ExpectedActorId}}"}""");

        var dto = row.ToAuditEntryDto();

        Assert.Equal(ExpectedActorId, dto.ActorId);
        Assert.Equal(eventType, dto.EventType);
    }

    [Fact]
    public void ToAuditEntryDto_UnknownEventType_ThrowsInvalidOperationException()
    {
        var row = new AuditRow(
            Guid.CreateVersion7(),
            "Ordinis.Domain.Tasks.SomeFutureEvent",
            OccurredAt,
            "{}");

        Assert.Throws<InvalidOperationException>(() => row.ToAuditEntryDto());
    }

    [Fact]
    public void ToAuditEntryDto_PayloadIsReadableAfterJsonDocumentWouldHaveBeenDisposed()
    {
        var taskId = Guid.CreateVersion7();
        var row = new AuditRow(
            Guid.CreateVersion7(),
            "Ordinis.Domain.Tasks.TaskCreated",
            OccurredAt,
            $$"""{"TaskId":"{{taskId}}","ReporterId":"{{ExpectedActorId}}"}""");

        var dto = row.ToAuditEntryDto();

        // Repression guard for a missing `.Clone()` in ProjectMapper.ToAuditEntryDto: without
        // it, reading a property off dto.Payload here throws ObjectDisposedException, because
        // the JsonDocument that owned this JsonElement went out of scope when the mapping
        // method returned.
        Assert.Equal(taskId, dto.Payload.GetProperty("TaskId").GetGuid());
    }
}
