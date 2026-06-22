using Ordinis.Domain.Tasks;

namespace Ordinis.UnitTests.Common.Builders;

/// <summary>
/// Creates valid <see cref="Comment"/> instances for unit tests.
/// </summary>
internal static class CommentBuilder
{
    public static Comment Create(
        Guid? taskId = null,
        Guid? authorId = null,
        string content = "Test comment") =>
        Comment.Create(
            taskId ?? Guid.NewGuid(),
            authorId ?? Guid.NewGuid(),
            content);
}
