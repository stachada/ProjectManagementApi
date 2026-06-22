using Ordinis.Domain.Projects;

namespace Ordinis.UnitTests.Common.Builders;

/// <summary>
/// Creates valid <see cref="Board"/> instances for unit tests.
/// </summary>
internal static class BoardBuilder
{
    public static Board Create(
        Guid? projectId = null,
        string name = "Test Board",
        Guid? createdByUserId = null) =>
        Board.Create(
            projectId ?? Guid.NewGuid(),
            name,
            createdByUserId ?? Guid.NewGuid());
}
