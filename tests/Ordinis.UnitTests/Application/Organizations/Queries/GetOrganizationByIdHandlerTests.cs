using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.Application.Organizations.Queries;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Queries;

/// <summary>
/// Verifies <see cref="GetOrganizationByIdHandler"/> returns the correct
/// <see cref="OrganizationDto"/>, resolves project count, and throws when not found.
/// </summary>
public class GetOrganizationByIdHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsCorrectDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(
            name: "Acme Corp",
            slug: "acme-corp",
            description: "Best org ever");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        OrganizationDto dto = await new GetOrganizationByIdHandler(db)
            .HandleAsync(new GetOrganizationById(org.Id));

        Assert.Equal(org.Id, dto.Id);
        Assert.Equal("Acme Corp", dto.Name);
        Assert.Equal("Best org ever", dto.Description);
        Assert.True(dto.IsActive);
        Assert.Equal(0, dto.ProjectCount);
    }

    [Fact]
    public async Task HandleAsync_OrganizationWithProjects_ReturnsCorrectProjectCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);

        // Two projects in this org, one in another org.
        db.Projects.Add(ProjectBuilder.Create(organizationId: org.Id, name: "P1"));
        db.Projects.Add(ProjectBuilder.Create(organizationId: org.Id, name: "P2"));
        db.Projects.Add(ProjectBuilder.Create(name: "Other org's project"));
        await db.SaveChangesAsync();

        OrganizationDto dto = await new GetOrganizationByIdHandler(db)
            .HandleAsync(new GetOrganizationById(org.Id));

        Assert.Equal(2, dto.ProjectCount);
    }

    [Fact]
    public async Task HandleAsync_SuspendedOrganization_ReturnsDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        org.Suspend();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        OrganizationDto dto = await new GetOrganizationByIdHandler(db)
            .HandleAsync(new GetOrganizationById(org.Id));

        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetOrganizationByIdHandler(db)
                .HandleAsync(new GetOrganizationById(Guid.CreateVersion7())));
    }
}
