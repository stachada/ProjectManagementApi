using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Queries;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Queries;

/// <summary>
/// Verifies <see cref="GetOrganizationProjectsHandler"/> returns paged projects
/// scoped to the organization, applies the <see cref="ProjectFilter"/> correctly,
/// and throws when the organization does not exist.
/// </summary>
public class GetOrganizationProjectsHandlerTests
{
    [Fact]
    public async Task HandleAsync_OrganizationWithProjects_ReturnsProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);

        db.Projects.Add(ProjectBuilder.Create(organizationId: org.Id, name: "Alpha"));
        db.Projects.Add(ProjectBuilder.Create(organizationId: org.Id, name: "Beta"));
        db.Projects.Add(ProjectBuilder.Create(name: "Other org's project"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetOrganizationProjectsHandler(db)
            .HandleAsync(new GetOrganizationProjects(org.Id));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, p => Assert.Contains(p.Name, new[] { "Alpha", "Beta" }));
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetOrganizationProjectsHandler(db)
                .HandleAsync(new GetOrganizationProjects(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_NoFilter_ExcludesArchivedProjectsByDefault()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);

        Project active = ProjectBuilder.Create(organizationId: org.Id, name: "Active");
        Project archived = ProjectBuilder.Create(organizationId: org.Id, name: "Archived");
        archived.Archive();
        db.Projects.AddRange(active, archived);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetOrganizationProjectsHandler(db)
            .HandleAsync(new GetOrganizationProjects(org.Id));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Active", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_IncludeArchived_ReturnsAllProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);

        Project active = ProjectBuilder.Create(organizationId: org.Id, name: "Active");
        Project archived = ProjectBuilder.Create(organizationId: org.Id, name: "Archived");
        archived.Archive();
        db.Projects.AddRange(active, archived);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetOrganizationProjectsHandler(db)
            .HandleAsync(new GetOrganizationProjects(
                org.Id,
                new ProjectFilter { IncludeArchived = true }));

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_MemberIdFilter_ScopesToMembership()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);

        var memberId = Guid.CreateVersion7();
        // memberId is the creator of proj1 (auto-added as Admin), not a member of proj2.
        Project proj1 = ProjectBuilder.Create(organizationId: org.Id, createdByUserId: memberId, name: "Member's Project");
        Project proj2 = ProjectBuilder.Create(organizationId: org.Id, name: "Other Project");
        db.Projects.AddRange(proj1, proj2);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetOrganizationProjectsHandler(db)
            .HandleAsync(new GetOrganizationProjects(
                org.Id,
                new ProjectFilter { MemberId = memberId }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Member's Project", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_Pagination_ReturnsCorrectPage()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);

        for (int i = 1; i <= 5; i++)
        {
            db.Projects.Add(ProjectBuilder.Create(organizationId: org.Id, name: $"Project {i}"));
        }
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetOrganizationProjectsHandler(db)
            .HandleAsync(new GetOrganizationProjects(
                org.Id,
                new ProjectFilter { Page = 2, PageSize = 2 }));

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
    }
}
