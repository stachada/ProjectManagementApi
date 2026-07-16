using System.Net;
using System.Net.Http.Json;
using Ordinis.Api.Users.Requests;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Application.Users.Dtos;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Users;

public sealed class UsersControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateUser_ValidRequest_Returns201WithLocationAndPersistedUser()
    {
        Guid organizationId = await SeedOrganizationAsync();
        var request = new CreateUserRequest(organizationId, "Alice", "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        UserDto? user = await response.Content.ReadFromJsonAsync<UserDto>();
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
        // caught by ReadFromJsonAsync<UserDto>() alone, since unknown properties are just ignored.
        Guid organizationId = await SeedOrganizationAsync();
        var request = new CreateUserRequest(organizationId, "Alice", "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        string rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateUser_NonexistentOrganization_Returns422()
    {
        var request = new CreateUserRequest(Guid.CreateVersion7(), "Alice", "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_SuspendedOrganization_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync(isSuspended: true);
        var request = new CreateUserRequest(organizationId, "Alice", "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmailInSameOrganization_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new CreateUserRequest(organizationId, "Alice Two", "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmailInDifferentOrganization_Returns201()
    {
        Guid organizationId1 = await SeedOrganizationAsync();
        Guid organizationId2 = await SeedOrganizationAsync();
        await SeedUserAsync(organizationId1, "Alice", "alice@example.com");
        var request = new CreateUserRequest(organizationId2, "Alice Two", "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_EmptyDisplayName_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        var request = new CreateUserRequest(organizationId, string.Empty, "alice@example.com", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_InvalidEmailFormat_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        var request = new CreateUserRequest(organizationId, "Alice", "not-an-email", "password123", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_PasswordTooShort_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        var request = new CreateUserRequest(organizationId, "Alice", "alice@example.com", "short", Role.Member);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/users", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingUser_ReturnsUser()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UserDto? user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal(userId, user!.Id);
        Assert.Equal("Alice", user.DisplayName);
    }

    [Fact]
    public async Task GetById_NonexistentUser_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_ExistingUser_ReturnsAssignedTasksAndSetsTotalCountHeader()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        Guid boardId = await SeedBoardAsync(organizationId, userId);
        await SeedTaskAsync(boardId, userId, "Assigned to Alice", assigneeId: userId);
        await SeedTaskAsync(boardId, userId, "Not assigned", assigneeId: null);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{userId}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>();
        Assert.NotNull(tasks);
        Assert.Single(tasks!);
        Assert.Equal("Assigned to Alice", tasks![0].Title);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetTasks_NonexistentUser_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/users/{Guid.CreateVersion7()}/tasks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_ValidRequest_Returns204AndPersistsChanges()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new UpdateUserRequest("Alice Updated", userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{userId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}");
        Assert.Equal("Alice Updated", user!.DisplayName);
    }

    [Fact]
    public async Task UpdateUser_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PutAsJsonAsync($"/api/v1/users/{userId}", new UpdateUserRequest("Name from request 1", userId)),
            () => Client.PutAsJsonAsync($"/api/v1/users/{userId}", new UpdateUserRequest("Name from request 2", userId)));
    }

    [Fact]
    public async Task UpdateUser_NonexistentUser_Returns404()
    {
        var request = new UpdateUserRequest("Alice Updated", Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_EmptyDisplayName_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new UpdateUserRequest(string.Empty, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{userId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ChangeOrgRole_ValidRequest_Returns204AndChangesRole()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com", Role.Member);
        var request = new ChangeUserOrgRoleRequest(Role.Admin, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{userId}/org-role", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}");
        Assert.Equal("Admin", user!.OrgRole);
    }

    [Fact]
    public async Task ChangeOrgRole_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com", Role.Member);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PutAsJsonAsync($"/api/v1/users/{userId}/org-role", new ChangeUserOrgRoleRequest(Role.Admin, userId)),
            () => Client.PutAsJsonAsync($"/api/v1/users/{userId}/org-role", new ChangeUserOrgRoleRequest(Role.Viewer, userId)));
    }

    [Fact]
    public async Task ChangeOrgRole_NonexistentUser_Returns404()
    {
        var request = new ChangeUserOrgRoleRequest(Role.Admin, Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}/org-role", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_ValidRequest_Returns204AndDeactivatesUser()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new DeactivateUserRequest(userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}");
        Assert.False(user!.IsActive);
    }

    [Fact]
    public async Task DeactivateUser_ConcurrentModification_Returns409()
    {
        // Two identical Deactivate calls race: the second (winner) sees the user still active in
        // the database (the first hasn't saved yet, since it's parked) and succeeds; the first
        // then resumes with a stale RowVersion and conflicts - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", new DeactivateUserRequest(userId)),
            () => Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", new DeactivateUserRequest(userId)));
    }

    [Fact]
    public async Task DeactivateUser_NonexistentUser_Returns404()
    {
        var request = new DeactivateUserRequest(Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}/deactivate", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_AlreadyInactive_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        await Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", new DeactivateUserRequest(userId));

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", new DeactivateUserRequest(userId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_ValidRequest_Returns204AndReactivatesUser()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        await Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", new DeactivateUserRequest(userId));
        var request = new ReactivateUserRequest(userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/users/{userId}/reactivate", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        UserDto? user = await Client.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}");
        Assert.True(user!.IsActive);
    }

    [Fact]
    public async Task ReactivateUser_ConcurrentModification_Returns409()
    {
        // Two identical Reactivate calls race, same pattern as DeactivateUser_ConcurrentModification_Returns409.
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        await Client.PostAsJsonAsync($"/api/v1/users/{userId}/deactivate", new DeactivateUserRequest(userId));

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsJsonAsync($"/api/v1/users/{userId}/reactivate", new ReactivateUserRequest(userId)),
            () => Client.PostAsJsonAsync($"/api/v1/users/{userId}/reactivate", new ReactivateUserRequest(userId)));
    }

    [Fact]
    public async Task ReactivateUser_NonexistentUser_Returns404()
    {
        var request = new ReactivateUserRequest(Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/users/{Guid.CreateVersion7()}/reactivate", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateUser_AlreadyActive_Returns422()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId, "Alice", "alice@example.com");
        var request = new ReactivateUserRequest(userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/users/{userId}/reactivate", request);

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
