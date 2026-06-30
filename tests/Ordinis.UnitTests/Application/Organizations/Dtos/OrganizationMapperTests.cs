using Ordinis.Application.Organizations.Dtos;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Dtos;

/// <summary>
/// Verifies <see cref="OrganizationMapper"/> field mapping. Pure function
/// test - no EF Core, no DI, no async.
/// </summary>
public class OrganizationMapperTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToDto_AllFieldsMapCorrectly()
    {
        Organization organization = OrganizationBuilder.Create(
            name: "Acme Corp",
            slug: "acme-corp",
            description: "Widget manufacturer");
        organization.CreatedAt = Now;

        OrganizationDto dto = organization.ToDto(projectCount: 12);

        Assert.Equal(organization.Id, dto.Id);
        Assert.Equal("Acme Corp", dto.Name);
        Assert.Equal("Widget manufacturer", dto.Description);
        Assert.True(dto.IsActive);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(12, dto.ProjectCount);
    }

    [Fact]
    public void ToDto_NoDescription_MapsToNull()
    {
        Organization organization = OrganizationBuilder.Create(description: null);

        OrganizationDto dto = organization.ToDto(projectCount: 0);

        Assert.Null(dto.Description);
    }

    [Fact]
    public void ToDto_ProjectCountParameterFlowsThrough()
    {
        Organization organization = OrganizationBuilder.Create();

        OrganizationDto noProjects = organization.ToDto(projectCount: 0);
        OrganizationDto manyProjects = organization.ToDto(projectCount: 99);

        Assert.Equal(0, noProjects.ProjectCount);
        Assert.Equal(99, manyProjects.ProjectCount);
    }

    [Fact]
    public void ToDto_SuspendedOrganization_IsActiveIsFalse()
    {
        Organization organization = OrganizationBuilder.Create();
        organization.Suspend();

        OrganizationDto dto = organization.ToDto(projectCount: 0);

        Assert.False(dto.IsActive);
    }
}
