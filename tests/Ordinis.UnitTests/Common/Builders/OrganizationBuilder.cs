using Ordinis.Domain.Organizations;

namespace Ordinis.UnitTests.Common.Builders;

/// <summary>
/// Creates valid <see cref="Organization"/> instances for unit tests.
/// </summary>
internal static class OrganizationBuilder
{
    public static Organization Create(
        string name = "Test Organization",
        string slug = "test-organization",
        string? description = null) =>
        Organization.Create(name, slug, description);
}
