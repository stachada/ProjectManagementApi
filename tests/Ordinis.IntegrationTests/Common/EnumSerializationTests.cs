using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Enums travel by name on the wire — in controller and minimal-API responses, in request bodies,
/// and in the audit trail's event payloads — while request bodies still accept the numeric form.
/// These tests read raw JSON on purpose: deserializing into a DTO would hide which form was sent.
/// </summary>
public sealed class EnumSerializationTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetTask_ReturnsStatusAndPriorityByName()
    {
        (Guid taskId, _) = await SeedTaskAsync(Priority.High);

        using JsonDocument task = await GetJsonAsync($"/api/v1/tasks/{taskId}");

        Assert.Equal("Backlog", task.RootElement.GetProperty("status").GetString());
        Assert.Equal("High", task.RootElement.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task GetProjectMembers_ReturnsRoleByName()
    {
        (_, Guid projectId) = await SeedTaskAsync(Priority.Medium);

        using JsonDocument members = await GetJsonAsync($"/api/v1/projects/{projectId}/members");

        Assert.Equal("Admin", Assert.Single(members.RootElement.EnumerateArray()).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Search_MinimalApiResponse_ReturnsStatusByName()
    {
        await SeedTaskAsync(Priority.Medium, title: "Searchable task");

        using JsonDocument results = await GetJsonAsync("/api/v1/search?type=tasks&q=Searchable");

        Assert.Equal("Backlog", Assert.Single(results.RootElement.EnumerateArray()).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("""{ "status": "ToDo" }""")]
    [InlineData("""{ "status": 1 }""")]
    public async Task Move_StatusByNameOrNumber_Returns204AndMovesTheTask(string body)
    {
        (Guid taskId, _) = await SeedTaskAsync(Priority.Medium);

        HttpResponseMessage response = await PostRawJsonAsync($"/api/v1/tasks/{taskId}/move", body, await GetTaskRowVersionAsync(taskId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        ProjectTaskStatus status = await SeedAsync(db => db.Tasks.Where(t => t.Id == taskId).Select(t => t.Status).SingleAsync());
        Assert.Equal(ProjectTaskStatus.ToDo, status);
    }

    [Fact]
    public async Task Move_UnknownStatusName_Returns400()
    {
        (Guid taskId, _) = await SeedTaskAsync(Priority.Medium);

        HttpResponseMessage response = await PostRawJsonAsync(
            $"/api/v1/tasks/{taskId}/move", """{ "status": "Finished" }""", await GetTaskRowVersionAsync(taskId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAudit_EventPayload_StatesStatusesByName()
    {
        (Guid taskId, Guid projectId) = await SeedTaskAsync(Priority.Medium);
        HttpResponseMessage move = await PostRawJsonAsync(
            $"/api/v1/tasks/{taskId}/move", """{ "status": "ToDo" }""", await GetTaskRowVersionAsync(taskId));
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);

        using JsonDocument audit = await GetJsonAsync($"/api/v1/projects/{projectId}/audit");

        JsonElement moved = audit.RootElement.EnumerateArray().First(e => e.GetProperty("eventType").GetString() == "TaskMoved");
        Assert.Equal("Backlog", moved.GetProperty("payload").GetProperty("PreviousStatus").GetString());
        Assert.Equal("ToDo", moved.GetProperty("payload").GetProperty("NewStatus").GetString());
    }

    // ---- Helpers ----

    /// <summary>A project whose only member (an Admin) is the authenticated caller, with one task.</summary>
    private async Task<(Guid TaskId, Guid ProjectId)> SeedTaskAsync(Priority priority, string title = "Fix login bug")
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();

        (Guid taskId, Guid projectId) = await SeedAsync(async db =>
        {
            Project project = Project.Create(organizationId, userId, "Apollo", "apollo", DateTimeOffset.UtcNow);
            Board board = Board.Create(project.Id, "Sprint 1", userId);
            ProjectTask task = ProjectTask.Create(board.Id, userId, title, DateTimeOffset.UtcNow, priority);
            db.Projects.Add(project);
            db.Boards.Add(board);
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return (task.Id, project.Id);
        });

        await AuthenticateAsUserAsync(userId);
        return (taskId, projectId);
    }

    private async Task<JsonDocument> GetJsonAsync(string requestUri)
    {
        HttpResponseMessage response = await Client.GetAsync(requestUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostRawJsonAsync(string requestUri, string json, byte[]? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{Convert.ToBase64String(ifMatch ?? [])}\"");
        return await Client.SendAsync(request);
    }
}
