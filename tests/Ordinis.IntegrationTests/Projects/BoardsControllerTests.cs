using System.Net;
using System.Net.Http.Json;
using Ordinis.Api.Projects.Requests;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Projects;

public sealed class BoardsControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndPersistedBoard()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        var request = new CreateBoardRequest("Sprint Board");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/boards", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        BoardDto? board = await response.Content.ReadFromJsonAsync<BoardDto>(ApiJson.Options);
        Assert.NotNull(board);
        Assert.Equal("Sprint Board", board!.Name);
        Assert.Equal(projectId, board.ProjectId);
        Assert.Equal(userId, board.CreatedByUserId);
        Assert.False(board.IsArchived);
        Assert.Equal($"/api/v1/boards/{board.Id}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Create_NonexistentProject_Returns404()
    {
        // CreateBoardValidator's ProjectId rule does an existence+not-archived MustAsync check,
        // so a missing project fails validation (422) before the handler ever runs - it never
        // gets the chance to throw a NotFoundException (404). Same class of bug already found in
        // TasksController.Create.
        var request = new CreateBoardRequest("Orphan Board");

        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{Guid.CreateVersion7()}/boards", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ArchivedProject_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        byte[]? projectIfMatch = await GetProjectRowVersionAsync(projectId);
        await SendAsync(HttpMethod.Post, $"/api/v1/projects/{projectId}/archive", projectIfMatch);
        var request = new CreateBoardRequest("Sprint Board");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/boards", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }


    [Fact]
    public async Task Create_DuplicateNameInSameProject_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        await SeedBoardAsync(projectId, userId, "Sprint Board");
        var request = new CreateBoardRequest("Sprint Board");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/boards", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyName_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        var request = new CreateBoardRequest(string.Empty);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/projects/{projectId}/boards", request);

        await AssertValidationProblemAsync(response, "Name");
    }

    [Fact]
    public async Task GetById_ExistingBoard_ReturnsBoard()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/boards/{boardId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        BoardDto? board = await response.Content.ReadFromJsonAsync<BoardDto>(ApiJson.Options);
        Assert.NotNull(board);
        Assert.Equal(boardId, board!.Id);
        Assert.Equal(0, board.TaskCount);
        Assert.Empty(board.Tasks);
    }

    [Fact]
    public async Task GetById_ExistingBoard_ReturnsETagHeaderMatchingConcurrencyToken()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/boards/{boardId}");

        BoardDto? board = await response.Content.ReadFromJsonAsync<BoardDto>(ApiJson.Options);
        Assert.NotNull(board);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal($"\"{board!.ConcurrencyToken}\"", response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task GetById_NonexistentBoard_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/boards/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_ExistingBoard_ReturnsTasksAndSetsTotalCountHeader()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        (Guid otherUserId, Guid otherProjectId) = await SeedProjectAsync();
        Guid otherBoardId = await SeedBoardAsync(otherProjectId, otherUserId); // logs out the other user, so we don't see their tasks in the first board's list
        await SeedTaskAsync(boardId, userId, "In scope");
        await SeedTaskAsync(otherBoardId, otherUserId, "Out of scope");

        await AuthenticateAsUserAsync(userId); // we are looking for tasks in the first board, so we need to be a member of that project

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/boards/{boardId}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        Assert.NotNull(tasks);
        Assert.Single(tasks!);
        Assert.Equal("In scope", tasks![0].Title);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Theory]
    [InlineData(false, new[] { "Due first", "Due second", "Due third" })]
    [InlineData(true, new[] { "Due third", "Due second", "Due first" })]
    public async Task GetTasks_SortByDueDate_OrdersByDueDateNotCreation(bool descending, string[] expectedTitles)
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        // Created in an order that differs from due-date order in both directions.
        await SeedTaskWithChildrenAsync(boardId, userId, "Due second", dueInDays: 20);
        await SeedTaskWithChildrenAsync(boardId, userId, "Due third", dueInDays: 30);
        await SeedTaskWithChildrenAsync(boardId, userId, "Due first", dueInDays: 10);
        await AuthenticateAsUserAsync(userId);

        HttpResponseMessage response = await Client.GetAsync(
            $"/api/v1/boards/{boardId}/tasks?sortBy=dueDate&sortDescending={descending}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        Assert.Equal(expectedTitles, tasks!.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task GetTasks_TasksWithCommentsAndAttachments_ReportsLiveCounts()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        await SeedTaskWithChildrenAsync(boardId, userId, "Has children", dueInDays: 5);
        await AuthenticateAsUserAsync(userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/boards/{boardId}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        TaskSummaryDto only = Assert.Single(tasks!);
        Assert.Equal(1, only.CommentCount); // soft-deleted comment excluded
        Assert.Equal(1, only.AttachmentCount);
    }

    [Fact]
    public async Task GetTasks_NonexistentBoard_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/boards/{Guid.CreateVersion7()}/tasks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenameBoard_ValidRequest_Returns204AndPersistsChanges()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        var request = new RenameBoardRequest("Renamed Board");
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        BoardDto? board = await Client.GetFromJsonAsync<BoardDto>($"/api/v1/boards/{boardId}", ApiJson.Options);
        Assert.Equal("Renamed Board", board!.Name);
    }

    [Fact]
    public async Task RenameBoard_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism. The two requests use
        // distinct names so neither trips RenameBoardValidator's duplicate-name 422 instead.
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        await AssertConcurrentRequestsConflictAsync(
            () => SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, new RenameBoardRequest("Name from request 1")),
            () => SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, new RenameBoardRequest("Name from request 2")));
    }

    [Fact]
    public async Task RenameBoard_NonexistentBoard_Returns404()
    {
        var request = new RenameBoardRequest("Renamed Board");

        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{Guid.CreateVersion7()}/name", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenameBoard_MissingIfMatch_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        var request = new RenameBoardRequest("Renamed Board");

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", null, request);

        await AssertValidationProblemAsync(response, "IfMatch");
    }

    [Fact]
    public async Task RenameBoard_StaleIfMatch_Returns409()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        var request = new RenameBoardRequest("Renamed Board");

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RenameBoard_DuplicateNameInSameProject_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        await SeedBoardAsync(projectId, userId, "Taken Name");
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        var request = new RenameBoardRequest("Taken Name");
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RenameBoard_EmptyName_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        var request = new RenameBoardRequest(string.Empty);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, request);

        await AssertValidationProblemAsync(response, "NewName");
    }

    [Fact]
    public async Task RenameBoard_NameTooLong_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId, "Original Name");
        var request = new RenameBoardRequest(new string('A', 101));
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, request);

        await AssertValidationProblemAsync(response, "NewName");
    }

    [Fact]
    public async Task RenameBoard_ArchivedBoard_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatchForArchive = await GetBoardRowVersionAsync(boardId);
        await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatchForArchive);
        var request = new RenameBoardRequest("Renamed Board");
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/boards/{boardId}/name", ifMatch, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveBoard_ActiveBoard_Returns204AndArchivesBoard()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatch);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        BoardDto? board = await Client.GetFromJsonAsync<BoardDto>($"/api/v1/boards/{boardId}", ApiJson.Options);
        Assert.True(board!.IsArchived);
    }

    [Fact]
    public async Task ArchiveBoard_ConcurrentModification_Returns409()
    {
        // Two identical Archive calls race: the second (winner) sees the board still active in
        // the database (the first hasn't saved yet, since it's parked) and succeeds; the first
        // then resumes with a stale RowVersion and conflicts - see
        // IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        await AssertConcurrentRequestsConflictAsync(
            () => SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatch),
            () => SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatch));
    }

    [Fact]
    public async Task ArchiveBoard_NonexistentBoard_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/boards/{Guid.CreateVersion7()}/archive", PlaceholderIfMatch);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveBoard_AlreadyArchivedBoard_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatchForFirstArchive = await GetBoardRowVersionAsync(boardId);
        await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatchForFirstArchive);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatch);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task UnarchiveBoard_ArchivedBoard_Returns204AndUnarchivesBoard()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatchForArchive = await GetBoardRowVersionAsync(boardId);
        await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatchForArchive);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/unarchive", ifMatch);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        BoardDto? board = await Client.GetFromJsonAsync<BoardDto>($"/api/v1/boards/{boardId}", ApiJson.Options);
        Assert.False(board!.IsArchived);
    }

    [Fact]
    public async Task UnarchiveBoard_ConcurrentModification_Returns409()
    {
        // Two identical Unarchive calls race, same pattern as ArchiveBoard_ConcurrentModification_Returns409.
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatchForArchive = await GetBoardRowVersionAsync(boardId);
        await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/archive", ifMatchForArchive);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        await AssertConcurrentRequestsConflictAsync(
            () => SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/unarchive", ifMatch),
            () => SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/unarchive", ifMatch));
    }

    [Fact]
    public async Task UnarchiveBoard_NonexistentBoard_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/boards/{Guid.CreateVersion7()}/unarchive", PlaceholderIfMatch);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnarchiveBoard_NotArchivedBoard_Returns422()
    {
        (Guid userId, Guid projectId) = await SeedProjectAsync();
        Guid boardId = await SeedBoardAsync(projectId, userId);
        byte[]? ifMatch = await GetBoardRowVersionAsync(boardId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/boards/{boardId}/unarchive", ifMatch);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task<(Guid UserId, Guid ProjectId)> SeedProjectAsync()
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();

        Guid projectId = await SeedAsync(async db =>
        {
            Project project = Project.Create(organizationId, userId, "Apollo", "apollo", DateTimeOffset.UtcNow);
            db.Projects.Add(project);

            await db.SaveChangesAsync();

            return project.Id;
        });

        await AuthenticateAsUserAsync(userId); // Ensure Client is authenticated as the project creator for subsequent requests

        return (userId, projectId);
    }

    private Task<Guid> SeedBoardAsync(Guid projectId, Guid createdByUserId, string name = "Sprint Board") => SeedAsync(async db =>
    {
        Board board = Board.Create(projectId, name, createdByUserId);
        db.Boards.Add(board);

        await db.SaveChangesAsync();

        return board.Id;
    });

    private Task<Guid> SeedTaskAsync(Guid boardId, Guid reporterId, string title = "Default task") => SeedAsync(async db =>
    {
        ProjectTask task = ProjectTask.Create(boardId, reporterId, title, DateTimeOffset.UtcNow);
        db.Tasks.Add(task);

        await db.SaveChangesAsync();

        return task.Id;
    });

    /// <summary>
    /// Seeds a task due in <paramref name="dueInDays"/> days with one live comment, one
    /// soft-deleted comment and one attachment - so list responses should report
    /// <c>CommentCount = 1</c> and <c>AttachmentCount = 1</c>.
    /// </summary>
    private Task<Guid> SeedTaskWithChildrenAsync(Guid boardId, Guid userId, string title, int dueInDays) => SeedAsync(async db =>
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProjectTask task = ProjectTask.Create(boardId, userId, title, now, dueDate: now.AddDays(dueInDays));
        task.AddComment("Kept", userId, now);
        Comment retracted = task.AddComment("Retracted", userId, now);
        task.RemoveComment(retracted.Id, userId, requesterIsModerator: false, now);
        task.AddAttachment("spec.pdf", "application/pdf", 1_024, "/attachments/spec.pdf", userId, now);
        db.Tasks.Add(task);

        await db.SaveChangesAsync();

        return task.Id;
    });
}
