using Ordinis.Domain.Tasks;

namespace Ordinis.UnitTests.Common.Builders;

/// <summary>
/// Creates valid <see cref="ProjectTask"/> instances for unit tests.
/// All parameters default to stable, non-meaningful values so individual
/// tests only need to supply what they're actually testing.
/// </summary>
internal static class TaskBuilder
{
    private static readonly DateTimeOffset DefaultNow = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DefaultDueDate = DefaultNow.AddDays(7);

    public static ProjectTask Create(
        Guid? boardId = null,
        Guid? reporterId = null,
        string title = "Default Task Title",
        string? description = null,
        Priority priority = Priority.Medium,
        DateTimeOffset? dueDate = null,
        DateTimeOffset? now = null) =>
        ProjectTask.Create(
            boardId ?? Guid.CreateVersion7(),
            reporterId ?? Guid.CreateVersion7(),
            title,
            now ?? DefaultNow,
            priority,
            description,
            dueDate ?? DefaultDueDate);

    /// <summary>
    /// Live (non-deleted) comment count <see cref="WithCountableChildren"/> leaves on a task.
    /// </summary>
    public const int ExpectedCommentCount = 1;

    /// <summary>
    /// Attachment count <see cref="WithCountableChildren"/> leaves on a task.
    /// </summary>
    public const int ExpectedAttachmentCount = 2;

    /// <summary>
    /// Adds two comments (one then soft-deleted) and two attachments, so list handlers'
    /// <c>CommentCount</c>/<c>AttachmentCount</c> can be asserted against
    /// <see cref="ExpectedCommentCount"/>/<see cref="ExpectedAttachmentCount"/>.
    /// </summary>
    /// <remarks>
    /// Call <c>db.ChangeTracker.Clear()</c> after saving and before running the handler: with
    /// the children still tracked, EF Core fixes up the navigation collections on any task the
    /// handler loads, and counts come out right even when the handler never queries them -
    /// exactly how a missing <c>Include</c> went unnoticed. A real request starts with an
    /// empty change tracker.
    /// </remarks>
    public static ProjectTask WithCountableChildren(this ProjectTask task)
    {
        Guid authorId = Guid.CreateVersion7();
        task.AddComment("Kept", authorId, DefaultNow);
        Comment retracted = task.AddComment("Retracted", authorId, DefaultNow);
        task.RemoveComment(retracted.Id, authorId, requesterIsModerator: false, DefaultNow);

        task.AddAttachment("a.png", "image/png", 100, "blob://a", authorId, DefaultNow);
        task.AddAttachment("b.png", "image/png", 200, "blob://b", authorId, DefaultNow);

        return task;
    }
}
