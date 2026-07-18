using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Projects.Requests;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Projects;

public sealed class ProjectsControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateProject_ValidRequest_Returns201WithLocationAndPersistedProject()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);

        var request = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: userId,
            Name: "Apollo",
            Description: "A project to land on the moon"
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ProjectDto? dto = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(dto);
        Assert.Equal(request.Name, dto!.Name);
        Assert.Equal(request.Description, dto.Description);
        Assert.Equal(request.OrganizationId, dto.OrganizationId);
        Assert.Equal(request.CreatedByUserId, dto.CreatedByUserId);
        Assert.Equal($"/api/v1/projects/{dto.Id}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task CreateProject_EmptyOrganizationId_Returns422()
    {
        var request = new CreateProjectRequest(
            OrganizationId: Guid.Empty,
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Apollo"
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_NotExistingOrganization_Returns422()
    {
        var request = new CreateProjectRequest(
            OrganizationId: Guid.CreateVersion7(),
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Apollo"
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_EmptyCreatedByUserId_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();

        var request = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: Guid.Empty,
            Name: "Apollo"
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_NotExistingCreatedByUser_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();

        var request = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: Guid.CreateVersion7(),
            Name: "Apollo"
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_EmptyName_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);

        var request = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: userId,
            Name: ""
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        await AssertValidationProblemAsync(response, "Name");
    }

    [Fact]
    public async Task CreateProject_DuplicateNameInSameOrganization_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);

        var request1 = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: userId,
            Name: "Apollo"
        );

        HttpResponseMessage response1 = await Client.PostAsJsonAsync("/api/v1/projects", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        var request2 = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: userId,
            Name: "Apollo"
        );

        HttpResponseMessage response2 = await Client.PostAsJsonAsync("/api/v1/projects", request2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response2.StatusCode);
    }

    [Fact]
    public async Task CreateProject_DescriptionTooLong_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);

        string longDescription = new string('A', 1001); // Assuming max length is 1000

        var request = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: userId,
            Name: "Apollo",
            Description: longDescription
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_ValidRequestWithMaxDescriptionLength_Returns201()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);

        string maxDescription = new string('A', 1000); // Assuming max length is 1000

        var request = new CreateProjectRequest(
            OrganizationId: organizationId,
            CreatedByUserId: userId,
            Name: "Apollo",
            Description: maxDescription
        );

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/projects", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ProjectDto? dto = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(dto);
        Assert.Equal(request.Description, dto!.Description);
    }

    [Fact]
    public async Task GetProjects_ValidRequest_ReturnsProjectsList()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        await SeedProjectAsync(organizationId, userId);
        await SeedProjectAsync(organizationId, userId, "Gemini", "gemini");

        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        IReadOnlyList<ProjectSummaryDto>? projects = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<ProjectSummaryDto>>();
        Assert.NotNull(projects);
        Assert.Equal(2, projects!.Count);
        Assert.True(getResponse.Headers.Contains("X-Total-Count"));
        Assert.Equal(projects.Count.ToString(), getResponse.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetProjects_EmptyDatabase_ReturnsEmptyList()
    {
        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        IReadOnlyList<ProjectSummaryDto>? projects = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<ProjectSummaryDto>>();
        Assert.NotNull(projects);
        Assert.Empty(projects);
        Assert.True(getResponse.Headers.Contains("X-Total-Count"));
        Assert.Equal("0", getResponse.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetById_ExistingProject_ReturnsProject()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        ProjectDto? fetchedDto = await getResponse.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(fetchedDto);
        Assert.Equal(projectId, fetchedDto!.Id);
        Assert.Equal("Apollo", fetchedDto.Name);
    }

    [Fact]
    public async Task GetById_NonExistingProject_Returns404()
    {
        Guid nonExistingProjectId = Guid.CreateVersion7();

        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects/{nonExistingProjectId}");

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetTasks_ValidRequest_ReturnsTasksList()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        var projectId = await SeedFullProjectAsync(organizationId, userId);

        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects/{projectId}/tasks");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        IReadOnlyList<TaskSummaryDto>? tasks = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<TaskSummaryDto>>();
        Assert.NotNull(tasks);
        Assert.Equal(2, tasks.Count);
        Assert.True(getResponse.Headers.Contains("X-Total-Count"));
        Assert.Equal(tasks.Count.ToString(), getResponse.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetTask_NonExistingProject_Returns404()
    {
        Guid nonExistingProjectId = Guid.CreateVersion7();

        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects/{nonExistingProjectId}/tasks");

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetMembers_ExistingProject_ReturnsMembers()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<ProjectMemberDto>? members = await response.Content.ReadFromJsonAsync<List<ProjectMemberDto>>();
        Assert.NotNull(members);
        // The creator is automatically added as an Admin member by Project.Create.
        Assert.Single(members!);
        Assert.Equal(userId, members![0].UserId);
        Assert.Equal(Role.Admin, members[0].Role);
    }

    [Fact]
    public async Task GetMembers_NonexistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{Guid.CreateVersion7()}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBoards_ExistingProject_ReturnsBoards()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedFullProjectAsync(organizationId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}/boards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<BoardSummaryDto>? boards = await response.Content.ReadFromJsonAsync<List<BoardSummaryDto>>();
        Assert.NotNull(boards);
        Assert.Equal(2, boards!.Count);
    }

    [Fact]
    public async Task GetBoards_NonexistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{Guid.CreateVersion7()}/boards");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ValidRequest_Returns204AndPersistsChanges()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        var request = new UpdateProjectRequest("Updated Name", "Updated Description");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        ProjectDto? project = await Client.GetFromJsonAsync<ProjectDto>($"/api/v1/projects/{projectId}");
        Assert.NotNull(project);
        Assert.Equal("Updated Name", project!.Name);
        Assert.Equal("Updated Description", project.Description);
    }

    [Fact]
    public async Task Update_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 1", "Description 1")),
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task Update_NonexistentProject_Returns404()
    {
        var request = new UpdateProjectRequest("Updated Name", null);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ArchivedProject_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);
        var request = new UpdateProjectRequest("Updated Name", null);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_EmptyName_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        var request = new UpdateProjectRequest(string.Empty, null);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_DescriptionOverMaxLength_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        string longDescription = new string('A', 1001);
        var request = new UpdateProjectRequest("Valid Name", longDescription);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", request);

        await AssertValidationProblemAsync(response, "NewDescription");
    }

    [Fact]
    public async Task Delete_ExistingProject_Returns204AndSoftDeletesProject()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_ConcurrentModification_Returns409()
    {
        // DeleteProjectHandler loads and saves the same Project aggregate as Update, so a
        // delete racing a concurrent update collides on RowVersion exactly like two updates do -
        // see IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.DeleteAsync($"/api/v1/projects/{projectId}"),
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task Delete_NonexistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/projects/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_ActiveProject_Returns204AndArchivesProject()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        ProjectDto? project = await Client.GetFromJsonAsync<ProjectDto>($"/api/v1/projects/{projectId}");
        Assert.NotNull(project);
        Assert.True(project!.IsArchived);
    }

    [Fact]
    public async Task Archive_ConcurrentModification_Returns409()
    {
        // ArchiveProjectHandler loads and saves the same Project aggregate as Update - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsync($"/api/v1/projects/{projectId}/archive", null),
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task Archive_NonexistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{Guid.CreateVersion7()}/archive", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_AlreadyArchivedProject_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Unarchive_ArchivedProject_Returns204AndUnarchivesProject()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{projectId}/unarchive", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        ProjectDto? project = await Client.GetFromJsonAsync<ProjectDto>($"/api/v1/projects/{projectId}");
        Assert.NotNull(project);
        Assert.False(project!.IsArchived);
    }

    [Fact]
    public async Task Unarchive_ConcurrentModification_Returns409()
    {
        // UnarchiveProjectHandler loads and saves the same Project aggregate as Delete - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync. Update can't be used as the
        // second (winner) request here: while request 1 is parked, the project is still archived
        // in the database (its own save hasn't landed yet), and Project.Rename/UpdateDescription
        // both call EnsureNotArchived - Delete is the only mutation that doesn't.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsync($"/api/v1/projects/{projectId}/unarchive", null),
            () => Client.DeleteAsync($"/api/v1/projects/{projectId}"));
    }

    [Fact]
    public async Task Unarchive_NonexistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{Guid.CreateVersion7()}/unarchive", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unarchive_NotArchivedProject_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{projectId}/unarchive", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_ValidRequest_Returns201WithLocation()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid newMemberId = await SeedUserAsync(organizationId);
        var request = new AddProjectMemberRequest(newMemberId, Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ProjectMemberDto? member = await response.Content.ReadFromJsonAsync<ProjectMemberDto>();
        Assert.NotNull(member);
        Assert.Equal(newMemberId, member!.UserId);
        Assert.Equal(Role.Member, member.Role);
        Assert.Equal($"/api/v1/projects/{projectId}/members", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task AddMember_ConcurrentModification_Returns409()
    {
        // AddProjectMemberHandler loads and saves the same Project aggregate as Update - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid newMemberId = await SeedUserAsync(organizationId);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new AddProjectMemberRequest(newMemberId, Role.Member)),
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task AddMember_NonexistentProject_Returns422()
    {
        // AddProjectMemberValidator does its own ProjectId existence MustAsync check, so a
        // missing project fails validation (422) before the handler ever runs and could throw
        // NotFoundException (404) - same class of bug already found in TasksController.Create.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        var request = new AddProjectMemberRequest(userId, Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{Guid.CreateVersion7()}/members", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_NonexistentUser_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        var request = new AddProjectMemberRequest(Guid.CreateVersion7(), Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_AlreadyAMember_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        // userId is already a member - it was auto-added as Admin by Project.Create.
        var request = new AddProjectMemberRequest(userId, Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_InvalidRole_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid newMemberId = await SeedUserAsync(organizationId);
        var request = new AddProjectMemberRequest(newMemberId, (Role)9999);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", request);

        await AssertValidationProblemAsync(response, "Role");
    }

    [Fact]
    public async Task ChangeMemberRole_ValidRequest_Returns204AndChangesRole()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid memberId = await SeedUserAsync(organizationId);
        await SeedProjectMemberAsync(projectId, memberId, Role.Member);
        var request = new ChangeMemberRoleRequest(Role.Viewer);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}/members/{memberId}/role", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<ProjectMemberDto>? members = await Client.GetFromJsonAsync<List<ProjectMemberDto>>($"/api/v1/projects/{projectId}/members");
        Assert.Equal(Role.Viewer, members!.Single(m => m.UserId == memberId).Role);
    }

    [Fact]
    public async Task ChangeMemberRole_ConcurrentModification_Returns409()
    {
        // ChangeMemberRoleHandler loads and saves the same Project aggregate as Update - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid memberId = await SeedUserAsync(organizationId);
        await SeedProjectMemberAsync(projectId, memberId, Role.Member);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}/members/{memberId}/role", new ChangeMemberRoleRequest(Role.Viewer)),
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task ChangeMemberRole_NonexistentProject_Returns404()
    {
        var request = new ChangeMemberRoleRequest(Role.Viewer);

        HttpResponseMessage response = await Client.PutAsJsonAsync(
            $"/api/v1/projects/{Guid.CreateVersion7()}/members/{Guid.CreateVersion7()}/role", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMemberRole_NotAMember_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid nonMemberId = await SeedUserAsync(organizationId);
        var request = new ChangeMemberRoleRequest(Role.Viewer);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}/members/{nonMemberId}/role", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMemberRole_InvalidRole_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid memberId = await SeedUserAsync(organizationId);
        await SeedProjectMemberAsync(projectId, memberId, Role.Member);
        var request = new ChangeMemberRoleRequest((Role)9999);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}/members/{memberId}/role", request);

        await AssertValidationProblemAsync(response, "NewRole");
    }

    [Fact]
    public async Task ChangeMemberRole_DemoteLastAdmin_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        // userId is the sole Admin - auto-added as Admin by Project.Create.
        var request = new ChangeMemberRoleRequest(Role.Member);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/projects/{projectId}/members/{userId}/role", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_ValidRequest_Returns204AndRemovesMember()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid memberId = await SeedUserAsync(organizationId);
        await SeedProjectMemberAsync(projectId, memberId, Role.Member);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/projects/{projectId}/members/{memberId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<ProjectMemberDto>? members = await Client.GetFromJsonAsync<List<ProjectMemberDto>>($"/api/v1/projects/{projectId}/members");
        Assert.DoesNotContain(members!, m => m.UserId == memberId);
    }

    [Fact]
    public async Task RemoveMember_ConcurrentModification_Returns409()
    {
        // RemoveProjectMemberHandler loads and saves the same Project aggregate as Update - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid memberId = await SeedUserAsync(organizationId);
        await SeedProjectMemberAsync(projectId, memberId, Role.Member);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.DeleteAsync($"/api/v1/projects/{projectId}/members/{memberId}"),
            () => Client.PutAsJsonAsync($"/api/v1/projects/{projectId}", new UpdateProjectRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task RemoveMember_NonexistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/projects/{Guid.CreateVersion7()}/members/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_NotAMember_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        Guid nonMemberId = await SeedUserAsync(organizationId);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/projects/{projectId}/members/{nonMemberId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_RemoveLastAdmin_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid projectId = await SeedProjectAsync(organizationId, userId);
        // userId is the sole Admin - auto-added as Admin by Project.Create.

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/projects/{projectId}/members/{userId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        // The seeded "Alice" user itself is unused here - every call site pairs this with its
        // own SeedUserAsync(organizationId) for the user it actually operates on - but
        // SeedOrganizationWithUserAsync still seeds one, matching this project's other seed
        // helpers that always create a valid owning user alongside a fresh organization.
        (Guid organizationId, _) = await SeedOrganizationWithUserAsync();
        return organizationId;
    }

    private Task<Guid> SeedUserAsync(Guid organizationId) => SeedAsync(async db =>
    {
        // Email must be unique per-organization - a random suffix lets this helper be called
        // more than once for the same organization within a single test.
        var email = $"bob-{Guid.CreateVersion7()}@example.com";
        User user = User.Create(organizationId, "Bob", email, "hashed-password");
        db.Users.Add(user);

        await db.SaveChangesAsync();
        return user.Id;
    });

    private Task<Guid> SeedProjectAsync(Guid organizationId, Guid userId, string projectName = "Apollo", string projectSlug = "apollo") => SeedAsync(async db =>
    {
        Project project = Project.Create(organizationId, userId, projectName, projectSlug, DateTimeOffset.UtcNow);
        db.Projects.Add(project);

        await db.SaveChangesAsync();
        return project.Id;
    });

    private Task<Guid> SeedProjectMemberAsync(Guid projectId, Guid userId, Role role) => SeedAsync(async db =>
    {
        Project project = await db.Projects.SingleAsync(p => p.Id == projectId);
        project.AddMember(userId, role, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
        return userId;
    });

    private Task<Guid> SeedFullProjectAsync(Guid organizationId, Guid userId, string projectName = "Apollo", string projectSlug = "apollo") => SeedAsync(async db =>
    {
        Project project = Project.Create(organizationId, userId, projectName, projectSlug, DateTimeOffset.UtcNow);
        db.Projects.Add(project);

        User memberUser = User.Create(organizationId, "Charlie", "charlie@example.com", "hashed-password");
        db.Users.Add(memberUser);
        project.AddMember(memberUser.Id, Role.Member, DateTimeOffset.UtcNow); // Add another user as a member of the project

        User viewerUser = User.Create(organizationId, "Dave", "dave@example.com", "hashed-password");
        db.Users.Add(viewerUser);
        project.AddMember(viewerUser.Id, Role.Viewer, DateTimeOffset.UtcNow); // Add another user as a viewer of the project

        Board mainBoard = Board.Create(project.Id, "Main Board", userId);
        db.Boards.Add(mainBoard);
        Board secondaryBoard = Board.Create(project.Id, "Secondary Board", userId);
        db.Boards.Add(secondaryBoard);

        ProjectTask task1 = ProjectTask.Create(mainBoard.Id, viewerUser.Id, "Task 1", DateTimeOffset.UtcNow);
        ProjectTask task2 = ProjectTask.Create(secondaryBoard.Id, memberUser.Id, "Task 2", DateTimeOffset.UtcNow);
        db.Tasks.AddRange(task1, task2);

        await db.SaveChangesAsync();
        return project.Id;
    });
}
