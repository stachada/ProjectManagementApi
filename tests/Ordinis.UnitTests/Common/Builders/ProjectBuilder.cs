using Ordinis.Domain.Projects;

namespace Ordinis.UnitTests.Common.Builders;

/// <summary>
/// Creates valid <see cref="Project"/> instances for unit tests.
/// </summary>
internal static class ProjectBuilder
{
    private static readonly DateTimeOffset DefaultNow =
        new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public static Project Create(
        Guid? organizationId = null,
        Guid? createdByUserId = null,
        string name = "Test Project",
        string slug = "test-project",
        string? description = null,
        DateTimeOffset? now = null) =>
        Project.Create(
            organizationId ?? Guid.CreateVersion7(),
            createdByUserId ?? Guid.CreateVersion7(),
            name,
            slug,
            now ?? DefaultNow,
            description);
}
