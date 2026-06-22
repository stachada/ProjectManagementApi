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
            boardId ?? Guid.NewGuid(),
            reporterId ?? Guid.NewGuid(),
            title,
            now ?? DefaultNow,
            priority,
            description,
            dueDate ?? DefaultDueDate);
}
