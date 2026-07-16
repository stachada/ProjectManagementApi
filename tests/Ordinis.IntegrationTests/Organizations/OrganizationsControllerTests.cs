using System.Net;
using System.Net.Http.Json;
using Ordinis.Api.Organizations.Requests;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Organizations;

public sealed class OrganizationsControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndPersistedOrganization()
    {
        var request = new CreateOrganizationRequest("Acme Corp", "Leading provider of anvils and other products.");

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/organizations", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        OrganizationDto? dto = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(dto);
        Assert.Equal(request.Name, dto.Name);
        Assert.Equal(request.Description, dto.Description);
        Assert.Equal($"/api/v1/organizations/{dto.Id}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Create_WithEmptyName_Returns422UnprocessableEntity()
    {
        var request = new CreateOrganizationRequest(string.Empty, "Description is optional.");

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/organizations", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDescription1001Chars_Returns422UnprocessableEntity()
    {
        string longDescription = new string('A', 1001);
        var request = new CreateOrganizationRequest("Valid Name", longDescription);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/organizations", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingOrganization_ReturnsOrganization()
    {
        Guid orgId = await SeedOrganizationAsync();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{orgId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        OrganizationDto? organization = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(organization);
        Assert.Equal(orgId, organization.Id);
        Assert.Equal("Acme", organization.Name);
    }

    [Fact]
    public async Task GetById_NonExistingOrganization_Returns404NotFound()
    {
        Guid nonExistingId = Guid.CreateVersion7();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{nonExistingId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProjects_ExistingOrganizationWithProjects_ReturnsPagedProjects()
    {
        int projectCount = 5;
        Guid orgId = await SeedOrganizationWithProjectsAsync(projectCount);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{orgId}/projects?page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<ProjectSummaryDto>? projects = await response.Content.ReadFromJsonAsync<IReadOnlyList<ProjectSummaryDto>>();
        Assert.NotNull(projects);
        Assert.Equal(2, projects.Count);
        Assert.True(response.Headers.Contains("X-Total-Count"));
        Assert.Equal(projectCount.ToString(), response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetProjects_NonExistingOrganization_Returns404NotFound()
    {
        Guid nonExistingId = Guid.CreateVersion7();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{nonExistingId}/projects");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProjects_ExistingOrganizationWithNoProjects_ReturnsEmptyList()
    {
        Guid orgId = await SeedOrganizationAsync();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{orgId}/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<ProjectSummaryDto>? projects = await response.Content.ReadFromJsonAsync<IReadOnlyList<ProjectSummaryDto>>();
        Assert.NotNull(projects);
        Assert.Empty(projects);
        Assert.True(response.Headers.Contains("X-Total-Count"));
        Assert.Equal("0", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task Update_ValidRequest_Returns204NoContentAndUpdatesOrganization()
    {
        Guid orgId = await SeedOrganizationAsync();
        var request = new UpdateOrganizationRequest("Updated Name", "Updated Description");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/organizations/{orgId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        OrganizationDto? updatedOrganization = await Client.GetFromJsonAsync<OrganizationDto>($"/api/v1/organizations/{orgId}");
        Assert.NotNull(updatedOrganization);
        Assert.Equal(request.Name, updatedOrganization.Name);
        Assert.Equal(request.Description, updatedOrganization.Description);
    }

    [Fact]
    public async Task Update_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        Guid orgId = await SeedOrganizationAsync();

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PutAsJsonAsync($"/api/v1/organizations/{orgId}", new UpdateOrganizationRequest("Name from request 1", "Description 1")),
            () => Client.PutAsJsonAsync($"/api/v1/organizations/{orgId}", new UpdateOrganizationRequest("Name from request 2", "Description 2")));
    }

    [Fact]
    public async Task Update_NonExistingOrganization_Returns404NotFound()
    {
        Guid nonExistingId = Guid.CreateVersion7();
        var request = new UpdateOrganizationRequest("Updated Name", "Updated Description");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/organizations/{nonExistingId}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_SuspendedOrganization_Returns422UnprocessableEntity()
    {
        Guid orgId1 = await SeedOrganizationAsync(isSuspended: true);
        var request = new UpdateOrganizationRequest("Acme", "Updated Description");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/organizations/{orgId1}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_ValidNameWithOverLengthDescription_Returns422AndDoesNotRenameOrganization()
    {
        // Regression test for a partial-write bug: Update used to send Rename and
        // UpdateDescription as two separate commands, each with its own SaveChangesAsync. A valid
        // name plus an over-length description committed the rename before the description update
        // failed validation - the request looked rejected (422) but the name was already changed.
        // UpdateOrganizationHandler now loads once and saves once, so this must leave the
        // organization completely untouched.
        Guid orgId = await SeedOrganizationAsync();
        var request = new UpdateOrganizationRequest("New Name", new string('a', 1001));

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/organizations/{orgId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        OrganizationDto? organization = await Client.GetFromJsonAsync<OrganizationDto>($"/api/v1/organizations/{orgId}");
        Assert.NotNull(organization);
        Assert.Equal("Acme", organization.Name);
    }

    [Fact]
    public async Task Suspend_ActiveOrganization_Returns204NoContentAndSuspendsOrganization()
    {
        Guid orgId = await SeedOrganizationAsync();

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{orgId}/suspend", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        OrganizationDto? updatedOrganization = await Client.GetFromJsonAsync<OrganizationDto>($"/api/v1/organizations/{orgId}");
        Assert.NotNull(updatedOrganization);
        Assert.False(updatedOrganization.IsActive);
    }

    [Fact]
    public async Task Suspend_ConcurrentModification_Returns409()
    {
        // Two identical Suspend calls race: the second (winner) sees the organization still
        // active in the database (the first hasn't saved yet, since it's parked) and succeeds;
        // the first then resumes with a stale RowVersion and conflicts - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        Guid orgId = await SeedOrganizationAsync();

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsync($"/api/v1/organizations/{orgId}/suspend", null),
            () => Client.PostAsync($"/api/v1/organizations/{orgId}/suspend", null));
    }

    [Fact]
    public async Task Suspend_NotExistingOrganization_Returns404NotFound()
    {
        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{Guid.CreateVersion7()}/suspend", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Suspend_AlreadySuspendedOrganization_Returns422UnprocessableEntity()
    {
        Guid orgId = await SeedOrganizationAsync(isSuspended: true);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{orgId}/suspend", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Reactivate_SuspendedOrganization_Returns204NoContentAndReactivatesOrganization()
    {
        Guid orgId = await SeedOrganizationAsync(isSuspended: true);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{orgId}/reactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        OrganizationDto? updatedOrganization = await Client.GetFromJsonAsync<OrganizationDto>($"/api/v1/organizations/{orgId}");
        Assert.NotNull(updatedOrganization);
        Assert.True(updatedOrganization.IsActive);
    }

    [Fact]
    public async Task Reactivate_ConcurrentModification_Returns409()
    {
        // Two identical Reactivate calls race, same pattern as Suspend_ConcurrentModification_Returns409.
        Guid orgId = await SeedOrganizationAsync(isSuspended: true);

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PostAsync($"/api/v1/organizations/{orgId}/reactivate", null),
            () => Client.PostAsync($"/api/v1/organizations/{orgId}/reactivate", null));
    }

    [Fact]
    public async Task Reactivate_NotExistingOrganization_Returns404NotFound()
    {
        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{Guid.CreateVersion7()}/reactivate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reactivate_AlreadyActiveOrganization_Returns422UnprocessableEntity()
    {
        Guid orgId = await SeedOrganizationAsync(isSuspended: false);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/organizations/{orgId}/reactivate", null);

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

    private Task<Guid> SeedOrganizationWithProjectsAsync(int projectCount) => SeedAsync(async db =>
    {
        Organization org = CreateOrganization();
        db.Organizations.Add(org);

        // "project-{i+1}" needs no extra slug suffix - Project.Slug is unique per organization,
        // and the index alone is already unique within this freshly created one.
        for (int i = 0; i < projectCount; i++)
        {
            User user = User.Create(org.Id, $"User {i + 1}", $"user{i + 1}@example.com", $"hashed-password-{i + 1}");
            db.Users.Add(user);
            Project project = Project.Create(org.Id, user.Id, $"Project {i + 1}", $"project-{i + 1}", DateTimeOffset.UtcNow);
            db.Projects.Add(project);
        }

        await db.SaveChangesAsync();

        return org.Id;
    });
}
