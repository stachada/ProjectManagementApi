using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Users.Requests;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Users.Dtos;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Users;

public sealed class UsersControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateUser_ValidRequest_Returns201WithLocationAndPersistedUser()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest("Alice", "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        UserDto? user = await response.Content.ReadFromJsonAsync<UserDto>(ApiJson.Options);
        Assert.NotNull(user);
        Assert.Equal("Alice", user!.DisplayName);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("Member", user.OrgRole);
        Assert.True(user.IsActive);
        Assert.Equal(organizationId, user.OrganizationId);
        Assert.Equal($"/api/v1/users/{user.Id}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task CreateUser_ResponseBody_NeverExposesPasswordOrHash()
    {
        // UserDto has no PasswordHash/RefreshToken properties, but assert against the raw JSON
        // too - a future property rename that happens to collide with the DTO shape wouldn't be
        // caught by ReadFromJsonAsync<UserDto>(ApiJson.Options) alone, since unknown properties are just ignored.
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest("Alice", "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        string rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateUser_ClientSuppliedOrganizationId_IsIgnoredAndUserJoinsCallersOrganization()
    {
        // Regression test for cross-tenant user creation: the organization used to come from the
        // body, so an Admin of one organization could create users in any other. The new user now
        // always joins the caller's own organization, whatever an outdated client sends.
        (Guid callerOrganizationId, _) = await AuthenticateAsOrgAdminAsync();
        Guid otherOrganizationId = await SeedOrganizationAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", new
        {
            OrganizationId = otherOrganizationId,
            DisplayName = "Mallory",
            Email = "mallory@example.com",
            Password = "password123",
            OrgRole = Role.Member
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        UserDto? user = await response.Content.ReadFromJsonAsync<UserDto>(ApiJson.Options);
        Assert.Equal(callerOrganizationId, user!.OrganizationId);
    }

    [Fact]
    public async Task CreateUser_SuspendedOrganization_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: true);
        var request = new CreateUserRequest("Alice", "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmailInSameOrganization_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new CreateUserRequest("Alice Two", "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmailInDifferentOrganization_Returns201()
    {
        (Guid organizationId1, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid organizationId2 = await SeedOrganizationAsync();
        await SeedUserAsync(organizationId2, "Alice", "alice@example.com");
        var request = new CreateUserRequest("Alice Two", "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_EmptyDisplayName_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest(string.Empty, "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        await AssertValidationProblemAsync(response, "DisplayName");
    }

    [Fact]
    public async Task CreateUser_DisplayNameTooLong_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest(new string('A', 101), "alice@example.com", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        await AssertValidationProblemAsync(response, "DisplayName");
    }

    [Fact]
    public async Task CreateUser_EmailTooLong_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        string longEmail = new string('a', 250) + "@x.co";
        var request = new CreateUserRequest("Alice", longEmail, "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        await AssertValidationProblemAsync(response, "Email");
    }

    [Fact]
    public async Task CreateUser_InvalidOrgRole_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest("Alice", "alice@example.com", "password123", (Role)9999);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        await AssertValidationProblemAsync(response, "OrgRole");
    }

    [Fact]
    public async Task CreateUser_InvalidEmailFormat_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest("Alice", "not-an-email", "password123", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_PasswordTooShort_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        var request = new CreateUserRequest("Alice", "alice@example.com", "short", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingUser_ReturnsUser()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UserDto? user = await response.Content.ReadFromJsonAsync<UserDto>(ApiJson.Options);
        Assert.NotNull(user);
        Assert.Equal(userId, user!.Id);
        Assert.Equal("Alice", user.DisplayName);
    }

    [Fact]
    public async Task GetById_NonexistentUser_Returns404()
    {
        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_UserInAnotherOrganization_Returns404EvenForOrgAdmin()
    {
        await AuthenticateAsOrgAdminAsync();
        Guid otherOrganizationId = await SeedOrganizationAsync();
        Guid otherUserId = await SeedUserAsync(otherOrganizationId, "Eve", "eve@example.com");

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{otherUserId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_UserInAnotherOrganization_Returns404()
    {
        await AuthenticateAsOrgAdminAsync();
        Guid otherOrganizationId = await SeedOrganizationAsync();
        Guid otherUserId = await SeedUserAsync(otherOrganizationId, "Eve", "eve@example.com");

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{otherUserId}/tasks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_CallerNotMemberOfTasksProject_ExcludesThoseTasks()
    {
        // Alice is assigned tasks in two projects; Bob (a plain org Member) belongs to only one.
        // Bob must not see Alice's task in the project he can't access.
        (Guid organizationId, Guid aliceId) = await SeedOrganizationWithUserAsync("Alice", "alice@example.com");
        Guid bobId = await SeedUserAsync(organizationId, "Bob", "bob@example.com");

        Guid sharedBoardId = await SeedBoardAsync(organizationId, bobId);
        Guid privateBoardId = await SeedBoardAsync(organizationId, aliceId);
        await SeedTaskAsync(sharedBoardId, bobId, "Visible to Bob", assigneeId: aliceId);
        await SeedTaskAsync(privateBoardId, aliceId, "Hidden from Bob", assigneeId: aliceId);

        await AuthenticateAsUserAsync(bobId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{aliceId}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        TaskSummaryDto task = Assert.Single(tasks!);
        Assert.Equal("Visible to Bob", task.Title);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetTasks_ExistingUser_ReturnsAssignedTasksAndSetsTotalCountHeader()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);

        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        Guid boardId = await SeedBoardAsync(organizationId, userId);
        await SeedTaskAsync(boardId, userId, "Assigned to Alice", assigneeId: userId);
        await SeedTaskAsync(boardId, userId, "Not assigned", assigneeId: null);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{userId}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        Assert.NotNull(tasks);
        Assert.Single(tasks!);
        Assert.Equal("Assigned to Alice", tasks![0].Title);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetTasks_NonexistentUser_Returns404()
    {
        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{Guid.CreateVersion7()}/tasks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_ValidRequest_Returns204AndPersistsChanges()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new UpdateUserRequest("Alice Updated");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}", ApiJson.Options);
        Assert.Equal("Alice Updated", user!.DisplayName);
    }

    [Fact]
    public async Task UpdateUser_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        await AssertConcurrentRequestsConflictAsync(
            () => SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}", new UpdateUserRequest("Name from request 1")),
            () => SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}", new UpdateUserRequest("Name from request 2")));
    }

    [Fact]
    public async Task UpdateUser_NonexistentUser_Returns404()
    {
        var request = new UpdateUserRequest("Alice Updated");

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_SelfUpdateByNonAdmin_Returns204()
    {
        Guid userId = await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}", new UpdateUserRequest("Alice Renamed"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}", ApiJson.Options);
        Assert.Equal("Alice Renamed", user!.DisplayName);
    }

    [Fact]
    public async Task UpdateUser_OtherUserByNonAdmin_Returns403AndLeavesNameUnchanged()
    {
        // Regression test: PUT /users/{id} used to accept any authenticated caller, so anyone
        // could rename anyone. Only the user themselves or an org Admin may now.
        (Guid organizationId, Guid aliceId) = await SeedOrganizationWithUserAsync("Alice", "alice@example.com");
        Guid bobId = await SeedUserAsync(organizationId, "Bob", "bob@example.com");
        await AuthenticateAsUserAsync(bobId);

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, aliceId, $"/api/v1/users/{aliceId}", new UpdateUserRequest("Renamed by Bob"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        UserDto? alice = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{aliceId}", ApiJson.Options);
        Assert.Equal("Alice", alice!.DisplayName);
    }

    [Fact]
    public async Task UpdateUser_EmptyDisplayName_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new UpdateUserRequest(string.Empty);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_DisplayNameTooLong_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new UpdateUserRequest(new string('A', 101));

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}", request);

        await AssertValidationProblemAsync(response, "DisplayName");
    }

    [Fact]
    public async Task ChangeOrgRole_ValidRequest_Returns204AndChangesRole()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com", Role.Member);
        var request = new ChangeUserOrgRoleRequest(Role.Admin);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}/org-role", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}", ApiJson.Options);
        Assert.Equal("Admin", user!.OrgRole);
    }

    [Fact]
    public async Task ChangeOrgRole_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com", Role.Member);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        await AssertConcurrentRequestsConflictAsync(
            () => SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}/org-role", new ChangeUserOrgRoleRequest(Role.Admin)),
            () => SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}/org-role", new ChangeUserOrgRoleRequest(Role.Viewer)));
    }

    [Fact]
    public async Task ChangeOrgRole_InvalidNewOrgRole_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com", Role.Member);
        var request = new ChangeUserOrgRoleRequest((Role)9999);

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, userId, $"/api/v1/users/{userId}/org-role", request);

        await AssertValidationProblemAsync(response, "NewOrgRole");
    }

    [Fact]
    public async Task ChangeOrgRole_NonexistentUser_Returns404()
    {
        var request = new ChangeUserOrgRoleRequest(Role.Admin);

        await AuthenticateAsOrgAdminAsync(); // Ensure Client is authenticated as an admin for subsequent requests

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}/org-role", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_ValidRequest_Returns204AndDeactivatesUser()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}", ApiJson.Options);
        Assert.False(user!.IsActive);
    }

    [Fact]
    public async Task DeactivateUser_ConcurrentModification_Returns409()
    {
        // Two identical Deactivate calls race: the second (winner) sees the user still active in
        // the database (the first hasn't saved yet, since it's parked) and succeeds; the first
        // then resumes with a stale RowVersion and conflicts - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        await AssertConcurrentRequestsConflictAsync(
            () => SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate"),
            () => SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate"));
    }

    [Fact]
    public async Task DeactivateUser_NonexistentUser_Returns404()
    {

        await AuthenticateAsOrgAdminAsync(); // Ensure Client is authenticated as an admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/users/{Guid.CreateVersion7()}/deactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_UserInAnotherOrganization_Returns404AndLeavesUserActive()
    {
        // Regression test for cross-tenant admin actions: [Authorize(Roles = "Admin")] alone let an
        // Admin of any organization deactivate users of every other organization.
        await AuthenticateAsOrgAdminAsync();
        Guid otherOrganizationId = await SeedOrganizationAsync();
        Guid otherUserId = await SeedUserAsync(otherOrganizationId, "Eve", "eve@example.com");

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Post, otherUserId, $"/api/v1/users/{otherUserId}/deactivate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        bool isActive = await SeedAsync(db => db.Users.Where(u => u.Id == otherUserId).Select(u => u.IsActive).SingleAsync());
        Assert.True(isActive);
    }

    [Fact]
    public async Task ChangeOrgRole_UserInAnotherOrganization_Returns404()
    {
        await AuthenticateAsOrgAdminAsync();
        Guid otherOrganizationId = await SeedOrganizationAsync();
        Guid otherUserId = await SeedUserAsync(otherOrganizationId, "Eve", "eve@example.com");

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Put, otherUserId, $"/api/v1/users/{otherUserId}/org-role", new ChangeUserOrgRoleRequest(Role.Admin));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_AlreadyInactive_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate");

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_ValidRequest_Returns204AndReactivatesUser()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate");

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/reactivate");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}", ApiJson.Options);
        Assert.True(user!.IsActive);
    }

    [Fact]
    public async Task ReactivateUser_ConcurrentModification_Returns409()
    {
        // Two identical Reactivate calls race, same pattern as DeactivateUser_ConcurrentModification_Returns409.
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/deactivate");

        await AssertConcurrentRequestsConflictAsync(
            () => SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/reactivate"),
            () => SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/reactivate"));
    }

    [Fact]
    public async Task ReactivateUser_NonexistentUser_Returns404()
    {

        await AuthenticateAsOrgAdminAsync(); // Ensure Client is authenticated as an admin for subsequent requests

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/users/{Guid.CreateVersion7()}/reactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_AlreadyActive_Returns422()
    {
        (Guid organizationId, Guid adminId) = await SeedOrganizationWithUserAsync(
            displayName: "Org Admin",
            email: "admin@example.com",
            orgRole: Role.Admin,
            isSuspended: false);
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AuthenticateAsUserAsync(adminId); // Ensure Client is authenticated as the organization's admin for subsequent requests

        HttpResponseMessage response = await SendWithCurrentUserVersionAsync(HttpMethod.Post, userId, $"/api/v1/users/{userId}/reactivate");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private Task<Guid> SeedOrganizationAsync(bool isSuspended = false) => SeedAsync(async db =>
    {
        Organization org = CreateOrganization();
        if (isSuspended)
        {
            org.Suspend();
        }
        db.Organizations.Add(org);

        await db.SaveChangesAsync();

        return org.Id;
    });

    private Task<Guid> SeedUserAsync(Guid organizationId, string displayName, string email, Role orgRole = Role.Member) => SeedAsync(async db =>
    {
        User user = User.Create(organizationId, displayName, email, "hashed-password", orgRole);
        db.Users.Add(user);

        await db.SaveChangesAsync();

        return user.Id;
    });

    private Task<Guid> SeedBoardAsync(Guid organizationId, Guid userId) => SeedAsync(async db =>
    {
        Project project = Project.Create(organizationId, userId, "Apollo", $"apollo-{Guid.CreateVersion7()}", DateTimeOffset.UtcNow);
        db.Projects.Add(project);

        Board board = Board.Create(project.Id, "Sprint Board", userId);
        db.Boards.Add(board);

        await db.SaveChangesAsync();

        return board.Id;
    });

    private Task<Guid> SeedTaskAsync(Guid boardId, Guid reporterId, string title, Guid? assigneeId) => SeedAsync(async db =>
    {
        ProjectTask task = ProjectTask.Create(boardId, reporterId, title, DateTimeOffset.UtcNow);
        if (assigneeId.HasValue)
        {
            task.Assign(assigneeId.Value, reporterId, DateTimeOffset.UtcNow);
        }
        db.Tasks.Add(task);

        await db.SaveChangesAsync();

        return task.Id;
    });
}
