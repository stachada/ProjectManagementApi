using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ordinis.Api.Tasks.Requests;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Tasks;

public sealed class TasksControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndPersistedTask()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Fix login bug", "Users can't sign in", Priority.High, null, null, userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(task);
        Assert.Equal("Fix login bug", task!.Title);
        Assert.Equal(boardId, task.BoardId);
        Assert.Equal(Priority.High, task.Priority);
        Assert.Equal(ProjectTaskStatus.Backlog, task.Status);
        Assert.Equal($"/api/v1/tasks/{task.Id}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Create_NonexistentBoard_Returns422()
    {
        // CreateTaskValidator's BoardId rule does an existence MustAsync check, so a missing
        // board fails validation (422) before the handler ever runs - it never gets the chance
        // to throw a NotFoundException (404).
        var request = new CreateTaskRequest(Guid.CreateVersion7(), "Orphan task", null, Priority.Medium, null, null, Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyTitle_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, string.Empty, null, Priority.Medium, null, null, userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "Title");
    }

    [Fact]
    public async Task Create_TitleTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, new string('A', 201), null, Priority.Medium, null, null, userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "Title");
    }

    [Fact]
    public async Task Create_InvalidPriority_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Valid title", null, (Priority)9999, null, null, userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "Priority");
    }

    [Fact]
    public async Task Create_NonexistentAssignee_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Valid title", null, Priority.Medium, Guid.CreateVersion7(), null, userId);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "AssigneeId");
    }

    [Fact]
    public async Task Create_NonexistentRequestedByUser_Returns422()
    {
        (_, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Valid title", null, Priority.Medium, null, null, Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "RequestedByUserId");
    }

    [Fact]
    public async Task GetById_ExistingTask_ReturnsTask()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Investigate outage");

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(task);
        Assert.Equal(taskId, task!.Id);
        Assert.Equal("Investigate outage", task.Title);
        Assert.Empty(task.Comments);
        Assert.Empty(task.Attachments);
    }

    [Fact]
    public async Task GetById_NonexistentTask_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_FilterByBoardId_ReturnsOnlyMatchingTasksAndSetsTotalCountHeader()
    {
        (Guid UserId, Guid BoardId)[] boards = await Task.WhenAll(SeedBoardAsync(), SeedBoardAsync());
        (Guid userId, Guid boardId) = boards[0];
        (Guid otherUserId, Guid otherBoardId) = boards[1];

        await Task.WhenAll(
            SeedTaskAsync(boardId, userId, "In scope"),
            SeedTaskAsync(otherBoardId, otherUserId, "Out of scope"));

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks?boardId={boardId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>();
        Assert.NotNull(tasks);
        Assert.Single(tasks!);
        Assert.Equal("In scope", tasks![0].Title);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task Update_ValidRequest_Returns204AndPersistsChanges()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Original title");
        var request = new UpdateTaskRequest("Updated title", "New description", Priority.Critical, null, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal("Updated title", task!.Title);
        Assert.Equal("New description", task.Description);
        Assert.Equal(Priority.Critical, task.Priority);
    }

    [Fact]
    public async Task Update_ConcurrentModification_OneRequestReturns409()
    {
        // No ETag/If-Match mechanism is wired at the HTTP layer, so a genuine 409 can only come
        // from a real RowVersion collision - see IntegrationTestBase.AssertConcurrentRequestsConflictAsync
        // and docs/INTEGRATION_TESTS.md for the deterministic mechanism.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Original title");

        await AssertConcurrentRequestsConflictAsync(
            () => Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", new UpdateTaskRequest("Title from request 1", null, Priority.Medium, null, userId)),
            () => Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", new UpdateTaskRequest("Title from request 2", null, Priority.Medium, null, userId)));
    }

    [Fact]
    public async Task Update_NonexistentTask_Returns404()
    {
        var request = new UpdateTaskRequest("Title", null, Priority.Medium, null, Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_EmptyTitle_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest(string.Empty, null, Priority.Medium, null, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_TitleTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest(new string('A', 201), null, Priority.Medium, null, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", request);

        await AssertValidationProblemAsync(response, "Title");
    }

    [Fact]
    public async Task Update_InvalidPriority_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest("Valid title", null, (Priority)9999, null, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", request);

        await AssertValidationProblemAsync(response, "Priority");
    }

    [Fact]
    public async Task Delete_ExistingTask_Returns204AndSoftDeletesTask()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}?requestedByUserId={userId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentTask_Returns404()
    {
        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{Guid.CreateVersion7()}?requestedByUserId={Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ConcurrentModification_Returns409()
    {
        // DeleteTaskHandler loads and saves the same ProjectTask aggregate as Update, so a
        // delete racing a concurrent update collides on RowVersion exactly like two updates do -
        // see IntegrationTestBase.AssertConcurrentRequestsConflictAsync.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Original title");

        await AssertConcurrentRequestsConflictAsync(
            () => Client.DeleteAsync($"/api/v1/tasks/{taskId}?requestedByUserId={userId}"),
            () => Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}", new UpdateTaskRequest("Title from request 2", null, Priority.Medium, null, userId)));
    }

    [Fact]
    public async Task AddComment_ValidRequest_Returns201WithLocation()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AddCommentRequest(userId, "Looks good to me");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CommentDto? comment = await response.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(comment);
        Assert.Equal("Looks good to me", comment!.Content);
        Assert.Equal($"/api/v1/tasks/{taskId}/comments", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task AddComment_NonexistentTask_Returns404()
    {
        var request = new AddCommentRequest(Guid.CreateVersion7(), "Comment on nothing");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{Guid.CreateVersion7()}/comments", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_EmptyContent_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AddCommentRequest(userId, string.Empty);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments/", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_ContentTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AddCommentRequest(userId, new string('A', 10_001));

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments/", request);

        await AssertValidationProblemAsync(response, "Content");
    }

    [Fact]
    public async Task EditComment_ValidRequest_Returns204AndPersistsChanges()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        var request = new EditCommentRequest("Edited comment", userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<CommentDto>? comments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/comments")).Content.ReadFromJsonAsync<List<CommentDto>>();
        CommentDto? comment = comments?.FirstOrDefault(c => c.Id == commentId);
        Assert.NotNull(comment);
        Assert.Equal("Edited comment", comment!.Content);
    }

    [Fact]
    public async Task EditComment_NonExistingTask_Returns422()
    {
        var request = new EditCommentRequest("Edited comment", Guid.CreateVersion7());

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{Guid.CreateVersion7()}/comments/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EditComment_NonexistentComment_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new EditCommentRequest("Edited comment", userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EditComment_EmptyNewContent_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        var request = new EditCommentRequest(string.Empty, userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EditComment_ContentTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        var request = new EditCommentRequest(new string('A', 10_001), userId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        await AssertValidationProblemAsync(response, "NewContent");
    }

    [Fact]
    public async Task EditComment_NonAuthorEditsSomeonesComment_Returns422()
    {
        // The combined task/comment/author MustAsync rule has no OverridePropertyName, so
        // FluentValidation keys it under the empty string "" - the same rule the
        // NonExistingTask/NonexistentComment tests above already trip, just via a different input.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        (_, Guid nonAuthorUserId) = await SeedOrganizationWithUserAsync("Bob", "bob@example.com");
        var request = new EditCommentRequest("Edited by someone else", nonAuthorUserId);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        await AssertValidationProblemAsync(response, string.Empty);
    }

    [Fact]
    public async Task RemoveComment_ExistingComment_Returns204AndRemovesComment()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Delete me");

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}/comments/{commentId}?requestedByUserId={userId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<CommentDto>? comments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/comments")).Content.ReadFromJsonAsync<List<CommentDto>>();
        Assert.DoesNotContain(comments!, c => c.Id == commentId);
    }

    [Fact]
    public async Task RemoveComment_NonexistentTask_Returns404()
    {
        HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/v1/tasks/{Guid.CreateVersion7()}/comments/{Guid.CreateVersion7()}?requestedByUserId={Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveComment_NonexistentComment_Returns422()
    {
        // RemoveComment has no validator existence check on CommentId - a missing comment on an
        // otherwise-real task reaches ProjectTask.RemoveComment's own domain guard, which throws
        // DomainException ("task.comment-not-found"), not NotFoundException - hence 422, not 404.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/v1/tasks/{taskId}/comments/{Guid.CreateVersion7()}?requestedByUserId={userId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddAttachment_ValidFile_Returns201WithDownloadUrl()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "notes.txt");
        content.Add(new StringContent(userId.ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AttachmentDto? attachment = await response.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(attachment);
        Assert.Equal("notes.txt", attachment!.FileName);
        Assert.NotEmpty(attachment.DownloadUrl);

        // Clean up the file LocalFileStorageService wrote to disk - Respawn only resets the database.
        await Client.DeleteAsync($"/api/v1/tasks/{taskId}/attachments/{attachment.Id}?removedByUserId={userId}");
    }

    [Fact]
    public async Task AddAttachment_NonexistentTask_Returns404()
    {
        // uploadedByUserId must be a real, existing user - AddAttachmentValidator checks that
        // existence itself (422 on failure), so a nonexistent user here would trip that rule
        // before the handler's own TaskId lookup (the thing this test targets) ever runs.
        (Guid userId, _) = await SeedBoardAsync();

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "notes.txt");
        content.Add(new StringContent(userId.ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{Guid.CreateVersion7()}/attachments", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // AddAttachmentValidator.FileName.NotEmpty() is unreachable via a real HTTP request, the same
    // way FileStream.NotNull() is: ASP.NET Core's IFormFile model binder itself rejects a
    // multipart file part with an empty filename with 400 Bad Request before the request ever
    // reaches the validator (confirmed empirically - manually constructing a Content-Disposition
    // header with an empty filename still gets a 400, not a 422). No integration test possible
    // for this specific rule without bypassing normal HTTP semantics.

    [Fact]
    public async Task AddAttachment_FileNameTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", new string('a', 256) + ".txt");
        content.Add(new StringContent(userId.ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        await AssertValidationProblemAsync(response, "FileName");
    }

    [Fact]
    public async Task AddAttachment_EmptyContentType_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        // Deliberately not setting fileContent.Headers.ContentType - IFormFile.ContentType comes
        // through as "" when the file part has no Content-Type header, which is what this test
        // needs to trigger AddAttachmentValidator's ContentType.NotEmpty() rule.
        content.Add(fileContent, "file", "notes.txt");
        content.Add(new StringContent(userId.ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        await AssertValidationProblemAsync(response, "ContentType");
    }

    [Fact]
    public async Task AddAttachment_ContentTypeTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/" + new string('a', 100));
        content.Add(fileContent, "file", "notes.txt");
        content.Add(new StringContent(userId.ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        await AssertValidationProblemAsync(response, "ContentType");
    }

    [Fact]
    public async Task AddAttachment_ZeroByteFile_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "empty.txt");
        content.Add(new StringContent(userId.ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        await AssertValidationProblemAsync(response, "SizeInBytes");
    }

    [Fact]
    public async Task AddAttachment_NonexistentUploadedByUser_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "notes.txt");
        content.Add(new StringContent(Guid.CreateVersion7().ToString()), "uploadedByUserId");

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        await AssertValidationProblemAsync(response, "UploadedByUserId");
    }

    [Fact]
    public async Task RemoveAttachment_ExistingAttachment_Returns204AndRemovesAttachment()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid attachmentId = await SeedAttachmentAsync(taskId, userId);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}/attachments/{attachmentId}?removedByUserId={userId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<AttachmentDto>? attachments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/attachments")).Content.ReadFromJsonAsync<List<AttachmentDto>>();
        Assert.DoesNotContain(attachments!, a => a.Id == attachmentId);
    }

    [Fact]
    public async Task RemoveAttachment_NonexistentTask_Returns404()
    {
        HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/v1/tasks/{Guid.CreateVersion7()}/attachments/{Guid.CreateVersion7()}?removedByUserId={Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAttachment_NonexistentAttachment_Returns404()
    {
        // Unlike RemoveComment, the handler checks attachment existence itself before delegating
        // to the domain and throws NotFoundException, so this is 404, not 422.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/v1/tasks/{taskId}/attachments/{Guid.CreateVersion7()}?removedByUserId={userId}");

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

    private Task<Guid> SeedCommentAsync(Guid taskId, Guid authorId, string content) => SeedAsync(async db =>
    {
        var task = await db.Tasks.FindAsync(taskId);
        Comment comment = task!.AddComment(content, authorId, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        return comment.Id;
    });

    private Task<Guid> SeedAttachmentAsync(Guid taskId, Guid uploadedByUserId) => SeedAsync(async db =>
    {
        var task = await db.Tasks.FindAsync(taskId);
        // storageUrl points at no real file - LocalFileStorageService.DeleteAsync no-ops (just
        // logs a warning) when the file is missing, so RemoveAttachment tests don't need an
        // actual upload on disk to exercise the DB/HTTP round trip.
        Attachment attachment = task!.AddAttachment(
            "seeded-file.txt", "text/plain", 4, "/attachments/seeded-file.txt", uploadedByUserId, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        return attachment.Id;
    });
}
