using System.Net;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Exercises the <c>[ResponseCache]</c> attribute added to single-resource <c>GetById</c> actions
/// in Phase 7 (see <c>Common/ApiServiceExtensions.cs</c>'s <c>services.AddResponseCaching()</c>
/// and <c>Program.cs</c>'s <c>app.UseResponseCaching()</c>, both already registered ahead of this
/// work). Only single-resource GETs opt in - list/collection endpoints deliberately do not, so
/// this also asserts one of those stays unaffected.
/// </summary>
public sealed class ResponseCachingTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetTaskById_ExistingTask_ReturnsCacheControlAndVaryHeaders()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.CacheControl.MaxAge);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.Contains("Authorization", response.Headers.Vary);
    }

    [Fact]
    public async Task GetOrganizationById_ExistingOrganization_ReturnsCacheControlAndVaryHeaders()
    {
        (Guid organizationId, _) = await SeedOrganizationWithUserAsync();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/organizations/{organizationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.CacheControl.MaxAge);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.Contains("Authorization", response.Headers.Vary);
    }

    [Fact]
    public async Task GetTaskById_RepeatedRequestWithinCacheWindow_StillReturnsCacheControlHeader()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage first = await Client.GetAsync($"/api/v1/tasks/{taskId}");
        HttpResponseMessage second = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(first.Headers.CacheControl);
        Assert.NotNull(second.Headers.CacheControl);
    }

    [Fact]
    public async Task GetTasks_ListEndpoint_HasNoCacheControlHeader()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks?boardId={boardId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.CacheControl);
    }

    private async Task<(Guid UserId, Guid BoardId)> SeedBoardAsync()
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();

        Guid boardId = await SeedAsync(async db =>
        {
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
