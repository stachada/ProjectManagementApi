using Ordinis.Application.Common;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Projects.Queries;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Queries;

public class GetProjectsFilteredHandlerTests
{
    private static readonly Guid TestOrganizationId = Guid.CreateVersion7();
    private static readonly ICurrentUserService AdminCurrentUser = new FakeCurrentUserService(Guid.CreateVersion7(), TestOrganizationId, isOrgAdmin: true);

    [Fact]
    public async Task HandleAsync_NoFilter_ExcludesArchivedByDefault()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project active = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Active");
        Project archived = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Archived");
        archived.Archive();
        db.Projects.AddRange(active, archived);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Active", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_IncludeArchived_ReturnsAllProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project active = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Active");
        Project archived = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Archived");
        archived.Archive();
        db.Projects.AddRange(active, archived);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { IncludeArchived = true }));

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_OrganizationIdFilter_ScopesToOrganization()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project inOrg = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "In Org");
        Project otherOrg = ProjectBuilder.Create(name: "Other Org"); // deliberately NOT TestOrganizationId
        db.Projects.AddRange(inOrg, otherOrg);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { OrganizationId = TestOrganizationId }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("In Org", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_MemberIdFilter_ScopesToMembership()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var userId = Guid.CreateVersion7();
        // userId is the creator of proj1 (auto-added as Admin), not a member of proj2.
        Project proj1 = ProjectBuilder.Create(organizationId: TestOrganizationId, createdByUserId: userId, name: "Member Project");
        Project proj2 = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Other Project");
        db.Projects.AddRange(proj1, proj2);
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { MemberId = userId }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Member Project", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.AddRange(
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Alpha"),
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Beta"),
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Gamma"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
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
    public async Task HandleAsync_SearchMatchesName_ReturnsOnlyMatchingProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.AddRange(
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Payment Gateway"),
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Onboarding Flow"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { Search = "gateway" }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Payment Gateway", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_SearchMatchesDescription_ReturnsOnlyMatchingProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.AddRange(
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Project A", description: "Handles payment gateway timeouts"),
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Project B", description: "Refresh onboarding screenshots"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { Search = "payment gateway" }));

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Project A", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_SearchIsCaseInsensitive_ReturnsMatchingProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.Add(ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Payment Gateway"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { Search = "GATEWAY" }));

        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_SearchNoMatch_ReturnsEmptyResult()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.Add(ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Payment Gateway"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { Search = "nonexistent" }));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_SearchWhitespaceOnly_ReturnsAllProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.AddRange(
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Payment Gateway"),
            ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Onboarding Flow"));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered(new ProjectFilter { Search = "   " }));

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_BoardAndMemberCountsProjectedCorrectly()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var createdByUserId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(organizationId: TestOrganizationId, createdByUserId: createdByUserId);
        project.AddMember(Guid.CreateVersion7(), Domain.Users.Role.Member,
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        db.Projects.Add(project);
        db.Boards.Add(BoardBuilder.Create(projectId: project.Id));
        db.Boards.Add(BoardBuilder.Create(projectId: project.Id));
        await db.SaveChangesAsync();

        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, AdminCurrentUser)
            .HandleAsync(new GetProjectsFiltered());

        ProjectSummaryDto dto = result.Items.Single();
        Assert.Equal(2, dto.MemberCount); // creator + 1 added
        Assert.Equal(2, dto.BoardCount);
    }

    [Fact]
    public async Task HandleAsync_NonAdminCallerIsProjectMember_ReturnsOnlyProjectsTheyBelongTo()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var callerId = Guid.CreateVersion7();

        Project memberProject = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Mine");
        memberProject.AddMember(callerId, Role.Member, new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        Project otherProject = ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Not Mine");
        db.Projects.AddRange(memberProject, otherProject);
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService(callerId, TestOrganizationId, isOrgAdmin: false);
        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, currentUser)
            .HandleAsync(new GetProjectsFiltered());

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Mine", result.Items.Single().Name);
    }

    [Fact]
    public async Task HandleAsync_NonAdminCallerIsNotAMember_ReturnsNoProjects()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        db.Projects.AddRange(ProjectBuilder.Create(organizationId: TestOrganizationId, name: "Someone Else's"));
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService(Guid.CreateVersion7(), TestOrganizationId, isOrgAdmin: false);
        PagedResult<ProjectSummaryDto> result = await new GetProjectsFilteredHandler(db, currentUser)
            .HandleAsync(new GetProjectsFiltered());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }
}
