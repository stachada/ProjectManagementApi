using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Ordinis.Api.Projects.Requests;
using Ordinis.Api.Tasks.Requests;
using Ordinis.Application.Common;
using Ordinis.Application.Projects.Queries;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.Infrastructure.Persistence;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Auth;

public class AuthorizationTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{

    [Fact]
    public async Task ProtectedEndpoint_NotToken_Returns401ProblemDetails()
    {
        // Act
        HttpResponseMessage response = await Client.GetAsync("/api/v1/users");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("A valid access token is required to access this resource.", content!.Detail);
    }

    [Fact]
    public async Task ProtectedEndpoint_TokenSignedWithDifferentKey_Returns401()
    {
        // Arrange
        (_, User user) = await SeedUserWithPasswordAsync("Password123!", Role.Member);

        var now = DateTimeOffset.UtcNow;
        var token = GenerateWrongAccessToken(user, now).AccessToken;

        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await Client.GetAsync("/api/v1/users");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("A valid access token is required to access this resource.", content!.Detail);
    }

    [Fact]
    public async Task ProtectedEndpoint_ExpiredToken_Returns401()
    {
        // Arrange
        (_, User user) = await SeedUserWithPasswordAsync("Password123!", Role.Member);

        var token = CreateExpiredAccessTokenAsync(user); // expired token

        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await Client.GetAsync("/api/v1/users");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("A valid access token is required to access this resource.", content!.Detail);
    }

    [Fact]
    public async Task TaskEndpoint_CallerNotProjectMember_Returns404IdenticalToMissingTask()
    {
        // Arrange
        (Organization organization, User user) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, User otherUser) = await SeedUserWithPasswordAsync("Password123!", Role.Member, organizationId: organization.Id);
        var taskId = await SeedProjectTaskAsync(organization.Id, otherUser.Id);

        await AuthenticateAsUserAsync(user.Id);

        // Act
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal($"ProjectTask '{taskId}' was not found.", content!.Detail);
        Assert.Equal($"Resource not found", content.Title);
    }

    [Fact]
    public async Task ProjectAdminAction_CallerIsPlainMember_Returns403()
    {
        // Arrange
        const string password = "Password123!";
        (Organization organization, User user) = await SeedUserWithPasswordAsync(password, Role.Member);
        var (nonAdminProjectUserId, projectId) = await SeedNonAdminProjectMemberAsync(organization.Id, user.Id);
        await AuthenticateAsUserAsync(nonAdminProjectUserId);

        // Act
        HttpResponseMessage response = await Client.PostAsync($"/api/v1/projects/{projectId}/archive", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("You do not have permission to perform this action.", content!.Detail);
    }

    [Fact]
    public async Task OrgAdminEndpoint_CallerIsNotAdmin_Returns403()
    {
        // Arrange
        const string password = "Password123!";
        (Organization organization, User user) = await SeedUserWithPasswordAsync(password, Role.Member);

        await AuthenticateAsUserAsync(user.Id);

        // Act
        HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/organizations/{organization.Id}/suspend",
            PlaceholderIfMatch,
            null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("You do not have permission to perform this action.", content!.Detail);
    }

    [Fact]
    public async Task Search_NoToken_Returns401()
    {
        // Act
        HttpResponseMessage response = await Client.GetAsync("/api/v1/search?query=login");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("A valid access token is required to access this resource.", content!.Detail);
    }

    [Fact]
    public async Task GetTaskById_CallerIsProjectViewer_Returns200()
    {
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId, Guid viewerId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Viewer);

        await AuthenticateAsUserAsync(viewerId);

        // Act
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateBoard_CallerIsProjectViewer_Returns403()
    {
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (Guid projectId, _, Guid taskId, Guid viewerId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Viewer);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AuthenticateAsUserAsync(viewerId);

        // Act
        HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/boards",
            ifMatch,
            new CreateBoardRequest("New Board"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_CallerIsProjectViewer_Returns403()
    {
        // Commenting has no assignee exception: TaskContributor requires Member+.
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId, Guid viewerId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Viewer);

        await AuthenticateAsUserAsync(viewerId);

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/v1/tasks/{taskId}/comments",
            new AddCommentRequest("Looks good"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddAttachment_CallerIsProjectViewer_Returns403()
    {
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId, Guid viewerId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Viewer);

        await AuthenticateAsUserAsync(viewerId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(new byte[] { 0x01, 0x02 });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "notes.txt"); // name = "file", plust a file name

        // Act
        HttpResponseMessage response = await Client.PostAsync(
            $"/api/v1/tasks/{taskId}/attachments",
            content);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("You do not have permission to perform this action.", problem!.Detail);
    }

    [Fact]
    public async Task UpdateTask_CallerIsAssigneeButNotProjectMember_Returns204()
    {
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId, Guid assigneeId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: null);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AuthenticateAsUserAsync(assigneeId);

        var request = new UpdateTaskRequest(
            Title: "Implement login (updated by assignee)",
            Description: null,
            Priority: Priority.High,
            DueDate: null);

        // Act
        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTask_CallerIsProjectViewerAndAssignee_Returns204()
    {
        // Same as abover with assigneeProjectRole: Role.Viewer: the assignee exception beats the Viewer
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId, Guid viewerId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Viewer);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AuthenticateAsUserAsync(viewerId);

        // Act
        HttpResponseMessage response = await SendAsync(
            HttpMethod.Put,
            $"/api/v1/tasks/{taskId}",
            ifMatch,
            new UpdateTaskRequest(
                Title: "Updated by viewer-assignee",
                Description: null,
                Priority: Priority.Low,
                DueDate: null));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CreateTask_CallerIsProjectViewer_Returns422()
    {
        // Arrange
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, Guid boardId, _, Guid viewerId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Viewer);

        await AuthenticateAsUserAsync(viewerId);

        var request = new CreateTaskRequest(
            BoardId: boardId, // just to have a valid board ID
            Title: "New Task",
            Description: "This is a new task.",
            Priority: Priority.Medium,
            AssigneeId: viewerId,
            DueDate: DateTimeOffset.UtcNow.AddDays(7));

        // Act
        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/", request);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetProject_CallerIsOrgAdminButNotProjectMember_Returns200()
    {
        // An org Admin sees every project of their organization in GET /projects, so opening one
        // must not 404 - project-scoped policies honour the org Admin role for the project's own
        // organization.
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, User orgAdmin) = await SeedUserWithPasswordAsync("Password123!", Role.Admin, organizationId: organization.Id);
        (_, Guid projectId) = await SeedNonAdminProjectMemberAsync(organization.Id, creator.Id);

        await AuthenticateAsUserAsync(orgAdmin.Id);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProject_CallerIsOrgAdminOfAnotherOrganization_Returns404()
    {
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, Guid projectId) = await SeedNonAdminProjectMemberAsync(organization.Id, creator.Id);
        (_, User otherOrgAdmin) = await SeedUserWithPasswordAsync("Password123!", Role.Admin);

        await AuthenticateAsUserAsync(otherOrgAdmin.Id);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTask_CallerIsOrgAdminButNotProjectMember_Returns204()
    {
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, User orgAdmin) = await SeedUserWithPasswordAsync("Password123!", Role.Admin, organizationId: organization.Id);
        (_, _, Guid taskId, _) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Member);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AuthenticateAsUserAsync(orgAdmin.Id);

        HttpResponseMessage response = await SendAsync(
            HttpMethod.Put,
            $"/api/v1/tasks/{taskId}",
            ifMatch,
            new UpdateTaskRequest("Updated by org admin", null, Priority.Low, null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganization_CallerFromAnotherOrganization_Returns404()
    {
        (Organization organization, _) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, User outsider) = await SeedUserWithPasswordAsync("Password123!", Role.Member);

        await AuthenticateAsUserAsync(outsider.Id);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{organization.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrganization_CallerIsAdminOfAnotherOrganization_Returns404AndLeavesNameUnchanged()
    {
        // Regression test for cross-tenant admin actions: [Authorize(Roles = "Admin")] alone let an
        // Admin of any organization rename, suspend, or reactivate every other organization.
        (Organization organization, _) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, User otherOrgAdmin) = await SeedUserWithPasswordAsync("Password123!", Role.Admin);

        await AuthenticateAsUserAsync(otherOrgAdmin.Id);

        HttpResponseMessage response = await Client.PutAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}",
            new { Name = "Hijacked", Description = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string name = await SeedAsync(db => db.Organizations.Where(o => o.Id == organization.Id).Select(o => o.Name).SingleAsync());
        Assert.Equal(organization.Name, name);
    }

    [Fact]
    public async Task SuspendOrganization_CallerIsPlainMemberOfThatOrganization_Returns403()
    {
        (Organization organization, User member) = await SeedUserWithPasswordAsync("Password123!", Role.Member);

        await AuthenticateAsUserAsync(member.Id);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{organization.Id}/suspend", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegisterWebhook_CallerIsPlainProjectMember_Returns403()
    {
        // Webhooks send project events to an external URL, so they require ProjectAdmin - they
        // previously had no policy at all beyond "authenticated".
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (Guid memberId, Guid projectId) = await SeedNonAdminProjectMemberAsync(organization.Id, creator.Id, projectRole: Role.Member);

        await AuthenticateAsUserAsync(memberId);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/webhooks",
            new { Url = "https://example.com/hook", EventTypes = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListWebhooks_CallerNotProjectMember_Returns404()
    {
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, Guid projectId) = await SeedNonAdminProjectMemberAsync(organization.Id, creator.Id);
        (_, User outsider) = await SeedUserWithPasswordAsync("Password123!", Role.Member, organizationId: organization.Id);

        await AuthenticateAsUserAsync(outsider.Id);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}/webhooks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_NonAdminCaller_ReturnsOnlyTasksFromTheirProjects()
    {
        // Arrange: two projects in the same organization, both created by `creator`.
        // memberId is a Member of the first project only.
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId, Guid memberId) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Member);
        await SeedTaskAssignedToNewUserAsync(
           organizationId: organization.Id,
           creatorId: creator.Id,
           assigneeProjectRole: Role.Member);

        await AuthenticateAsUserAsync(memberId);

        // Act: no boardId filter, so only caller scoping can exclude the second project's task
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>();
        Assert.NotNull(tasks);
        TaskSummaryDto only = Assert.Single(tasks!);
        Assert.Equal(taskId, only.Id);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetTasks_CallerIsMemberOfBothProjects_ReturnsTasksFromBoth()
    {
        // Arrange: two projects in the same organization, both created by `creator`.
        (Organization organization, User creator) = await SeedUserWithPasswordAsync("Password123!", Role.Member);
        (_, _, Guid taskId1, _) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Member);
        (_, _, Guid taskId2, _) = await SeedTaskAssignedToNewUserAsync(
            organizationId: organization.Id,
            creatorId: creator.Id,
            assigneeProjectRole: Role.Member);

        await AuthenticateAsUserAsync(creator.Id);

        // Act: no boardId filter, so both projects' tasks should be returned
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>();
        Assert.NotNull(tasks);
        Assert.Equal(new[] { taskId1, taskId2 }.Order(), tasks!.Select(t => t.Id).Order());
        Assert.Equal("2", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task AttachemntFile_NoToken_Returns401()
    {
        // Act
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/00000000-0000-0000-0000-000000000000/attachments/00000000-0000-0000-0000-000000000000/file");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("A valid access token is required to access this resource.", content!.Detail);
    }

    [Fact]
    public async Task UnmatchedRoute_NoToken_Returns401()
    {
        // Act
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/nonexistent-route");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(content);
        Assert.Equal("A valid access token is required to access this resource.", content!.Detail);
    }

    private string CreateExpiredAccessTokenAsync(User user)
    {
        using IServiceScope scope = CreateScope();
        IJwtTokenService tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        TimeProvider time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        (string accessToken, _) = tokens.GenerateAccessToken(user, time.GetUtcNow().AddHours(-1)); // expired token

        return accessToken;
    }

    private async Task<(Organization Organization, User User)> SeedUserWithPasswordAsync(
        string password,
        Role orgRole = Role.Member,
        bool deactivated = false,
        Guid? organizationId = null) => await SeedAsync(async db =>
    {
        using IServiceScope scope = CreateScope();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var organization = organizationId is null
            ? Organization.Create(
                name: $"Organization-{Guid.CreateVersion7()}",
                slug: $"organization-{Guid.CreateVersion7()}",
                description: "Test organization")
            : await db.Organizations.SingleAsync(o => o.Id == organizationId);
        if (organizationId is null)
        {
            db.Organizations.Add(organization);
        }

        var user = User.Create(
            organizationId: organization.Id,
            displayName: "John Doe",
            email: $"user-{Guid.NewGuid()}@example.com",
            passwordHash: passwordHasher.Hash(password),
            orgRole: orgRole);

        if (deactivated)
        {
            user.Deactivate();
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (organization, user);
    });

    private async Task<Guid> SeedProjectTaskAsync(
        Guid organizationId,
        Guid userId) => await SeedAsync(async db =>
    {
        Project project = Project.Create(
            organizationId,
            userId,
            "Apollo",
            "apolloo",
            DateTimeOffset.UtcNow);
        db.Projects.Add(project);

        Board board = Board.Create(
            projectId: project.Id,
            name: "Backlog",
            createdByUserId: userId);
        db.Boards.Add(board);

        ProjectTask task = ProjectTask.Create(
            boardId: board.Id,
            reporterId: userId,
            title: "Implement login",
            now: DateTimeOffset.UtcNow,
            description: "Implement the login endpoint and authentication logic.");
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return task.Id;
    });

    private async Task<(Guid ProjectId, Guid BoardId, Guid TaskId, Guid AssigneeId)> SeedTaskAssignedToNewUserAsync(
        Guid organizationId,
        Guid creatorId,
        Role? assigneeProjectRole) => await SeedAsync(async db =>
    {
        var assignee = User.Create(
            organizationId: organizationId,
            displayName: "Jame Smit",
            email: $"jane-{Guid.NewGuid()}.smitm@example.com",
            passwordHash: "irrelevant",
            orgRole: Role.Member);
        db.Users.Add(assignee);

        Project project = Project.Create(
            organizationId,
            creatorId,
            "Apollo",
            $"apollo-{Guid.CreateVersion7()}",
            DateTimeOffset.UtcNow);
        if (assigneeProjectRole is not null)
        {
            project.AddMember(
                assignee.Id,
                assigneeProjectRole.Value,
                DateTimeOffset.UtcNow);
        }
        db.Projects.Add(project);

        Board board = Board.Create(
            projectId: project.Id,
            name: "Backlog",
            createdByUserId: creatorId);
        db.Boards.Add(board);

        ProjectTask task = ProjectTask.Create(
            boardId: board.Id,
            reporterId: creatorId,
            title: "Implement login",
            now: DateTimeOffset.UtcNow,
            description: "Implement the login endpoint and authentication logic.");
        task.Assign(assignee.Id, creatorId, DateTimeOffset.UtcNow);
        db.Tasks.Add(task);

        await db.SaveChangesAsync();
        return (project.Id, board.Id, task.Id, assignee.Id);
    });

    private async Task<(Guid NonAdminProjectUserId, Guid ProjectId)> SeedNonAdminProjectMemberAsync(
        Guid organizationId,
        Guid projectAdminUserId,
        string displayName = "Jane Smith",
        string email = "jane.smith@example.com",
        Role projectRole = Role.Member) => await SeedAsync(async db =>
    {
        Project project = Project.Create(
           organizationId,
           projectAdminUserId,
           "Apollo",
           "apollo", DateTimeOffset.UtcNow);
        db.Projects.Add(project);

        var nonAdminProjectUser = User.Create(
            organizationId: organizationId,
            displayName: displayName,
            email: email,
            passwordHash: "irrelevant",
            orgRole: Role.Member);
        db.Users.Add(nonAdminProjectUser);

        project.AddMember(nonAdminProjectUser.Id, projectRole, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
        return (nonAdminProjectUser.Id, project.Id);
    });

    private (string AccessToken, DateTimeOffset ExpiresAt) GenerateWrongAccessToken(User user, DateTimeOffset now)
    {
        // random key generated for testing purposes, not the same as the one used in the application
        var key = new SymmetricSecurityKey(Convert.FromBase64String("MJBiDByt7RFE3350aat3BNDWFCgJrwzdC0+9VDTGJWWpZk5R7QkqhPjoVcB3XAjly/p3IW3FI/n5TKzuDg6DVw=="));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        DateTimeOffset expiresAt = now.AddMinutes(15);

        Claim[] claims =
        [
            new (JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new (JwtRegisteredClaimNames.Email, user.Email),
            new ("name", user.DisplayName),
            new ("org_id", user.OrganizationId.ToString()),
            new (ClaimTypes.Role, user.OrgRole.ToString()),
            new (JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        ];

        var token = new JwtSecurityToken(
            issuer: "Ordinis", // Intentionally wrong issuer
            audience: "Ordinis.Api", // Intentionally wrong audience
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
