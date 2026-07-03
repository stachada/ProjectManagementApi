using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetProjectsFilteredHandlerTests
{
    [Fact]
    public async Task HandleAsync_NoFilter_ExcludesArchivedByDefault()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project active = ProjectBuilder.Create(name: "Active");
        Project archived = ProjectBuilder.Create(name: "Archived");
        archived.Archive();
        db.Projects.AddRange(active, archived);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db)
            .HandleAsync(new GetProjectsFiltered());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Active", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_IncludeArchived_ReturnsAllProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project active = ProjectBuilder.Create(name: "Active");
        Project archived = ProjectBuilder.Create(name: "Archived");
        archived.Archive();
        db.Projects.AddRange(active, archived);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { IncludeArchived = true }));

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_OrganizationIdFilter_ScopesToOrganization()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var orgId = Guid.CreateVersion7();
        Project inOrg = ProjectBuilder.Create(organizationId: orgId, name: "In Org");
        Project otherOrg = ProjectBuilder.Create(name: "Other Org");
        db.Projects.AddRange(inOrg, otherOrg);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { OrganizationId = orgId }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("In Org", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_MemberIdFilter_ScopesToMembership()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var userId = Guid.CreateVersion7();
        // userId is the creator of proj1 (auto-added as Admin), not a member of proj2.
        Project proj1 = ProjectBuilder.Create(createdByUserId: userId, name: "Member Project");
        Project proj2 = ProjectBuilder.Create(name: "Other Project");
        db.Projects.AddRange(proj1, proj2);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { MemberId = userId }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Member Project", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.AddRange(
            ProjectBuilder.Create(name: "Alpha"),
            ProjectBuilder.Create(name: "Beta"),
            ProjectBuilder.Create(name: "Gamma"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter
            {
                SortBy = "name",
                Page = 2,
                PageSize = 1
            }));

        Assert.Equal(3, result.TotalCount);
        ProjectSummaryDto page = Assert.Single(result.Items);
        Assert.Equal("Beta", page.Name);
    }

    [Fact]
    public async Task HandleAsync_BoardAndMemberCountsProjectedCorrectly()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var createdByUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: createdByUserId);
        project.AddMember(Guid.CreateVersion7(), Domain.Users.Role.Member,
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        db.Projects.Add(project);
        db.Boards.Add(BoardBuilder.Create(projectId: project.Id));
        db.Boards.Add(BoardBuilder.Create(projectId: project.Id));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db)
            .HandleAsync(new GetProjectsFiltered());

        ProjectSummaryDto dto = result.Items.Single();
        Assert.Equal(2, dto.MemberCount); // creator + 1 added
        Assert.Equal(2, dto.BoardCount);
    }
}
