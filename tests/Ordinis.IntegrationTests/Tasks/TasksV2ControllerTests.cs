using System.Net;
using System.Net.Http.Json;
using Ordinis.Application.Common;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Tasks;

public sealed class TasksV2ControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetById_ExistingTask_ReturnsSameFieldsAsV1PlusBoardLink()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Investigate outage");

        HttpResponseMessage v1Response = await Client.GetAsync($"/api/v1/tasks/{taskId}");
        HttpResponseMessage v2Response = await Client.GetAsync($"/api/v2/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, v2Response.StatusCode);
        TaskDto? v1Task = await v1Response.Content.ReadFromJsonAsync<TaskDto>();
        TaskDto? v2Task = await v2Response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(v1Task);
        Assert.NotNull(v2Task);
        Assert.Equal(v1Task!.Id, v2Task!.Id);
        Assert.Equal(v1Task.Title, v2Task.Title);
        Assert.Equal(v1Task.BoardId, v2Task.BoardId);
        Assert.Equal(v1Task.ConcurrencyToken, v2Task.ConcurrencyToken);

        // v2's only difference from v1: every v1 link is still present...
        foreach (HateoasLink link in v1Task.Links)
        {
            Assert.Contains(v2Task.Links, l => l.Rel == link.Rel && l.Href == link.Href && l.Method == link.Method);
        }

        // ...plus one extra `board` link that v1 does not return.
        Assert.DoesNotContain(v1Task.Links, l => l.Rel == "board");
        Assert.Contains(v2Task.Links, l => l.Rel == "board" && l.Href == $"/api/v1/boards/{boardId}" && l.Method == "GET");
    }

    [Fact]
    public async Task GetById_ExistingTask_ReturnsETagHeaderMatchingConcurrencyToken()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v2/tasks/{taskId}");

        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(task);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal($"\"{task!.ConcurrencyToken}\"", response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task GetById_NonexistentTask_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v2/tasks/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid UserId, Guid BoardId)> SeedBoardAsync()
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();

        Guid boardId = await SeedAsync(async db =>
        {
            // "apollo" needs no slug suffix - Project.Slug is unique per organization, and this
            // is the only project in a freshly created one.
            // user will be the single project member, with the role of Admin, which is the
            // default in Project.Create.
            Project project = Project.Create(organizationId, userId, "Apollo", "apollo", DateTimeOffset.UtcNow);
            db.Projects.Add(project);

            Board board = Board.Create(project.Id, "Sprint Board", userId);
            db.Boards.Add(board);

            await db.SaveChangesAsync();

            return board.Id;
        });

        return (userId, boardId);
    }

    private Task<Guid> SeedTaskAsync(Guid boardId, Guid reporterId, string title = "Default task") => SeedAsync(async db =>
    {
        ProjectTask task = ProjectTask.Create(boardId, reporterId, title, DateTimeOffset.UtcNow);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return task.Id;
    });
}
