using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Tasks.Requests;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Tasks;

public sealed class TasksControllerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndPersistedTask()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Fix login bug", "Users can't sign in", Priority.High, null, null);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.NotNull(task);
        Assert.Equal("Fix login bug", task!.Title);
        Assert.Equal(boardId, task.BoardId);
        Assert.Equal(Priority.High, task.Priority);
        Assert.Equal(ProjectTaskStatus.Backlog, task.Status);
        Assert.Equal($"/api/v1/tasks/{task.Id}", response.Headers.Location?.PathAndQuery);

        // The reporter is the authenticated caller - the request body carries no user ID at all.
        Guid reporterId = await SeedAsync(db => db.Tasks.Where(t => t.Id == task.Id).Select(t => t.ReporterId).SingleAsync());
        Assert.Equal(userId, reporterId);
    }

    [Fact]
    public async Task Create_NonexistentBoard_Returns422()
    {
        // CreateTaskValidator's BoardId rule does an existence MustAsync check, so a missing
        // board fails validation (422) before the handler ever runs - it never gets the chance
        // to throw a NotFoundException (404).
        var request = new CreateTaskRequest(Guid.CreateVersion7(), "Orphan task", null, Priority.Medium, null, null);

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 422 instead of a 401

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyTitle_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, string.Empty, null, Priority.Medium, null, null);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "Title");
    }

    [Fact]
    public async Task Create_TitleTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, new string('A', 201), null, Priority.Medium, null, null);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "Title");
    }

    [Fact]
    public async Task Create_InvalidPriority_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Valid title", null, (Priority)9999, null, null);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "Priority");
    }

    [Fact]
    public async Task Create_NonexistentAssignee_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        var request = new CreateTaskRequest(boardId, "Valid title", null, Priority.Medium, Guid.CreateVersion7(), null);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "AssigneeId");
    }

    [Fact]
    public async Task Create_CallerNotMemberOfBoardsProject_Returns422()
    {
        // Regression test for caller impersonation: the acting user used to come from the body,
        // so a non-member could create tasks on any board by sending a real member's ID. It now
        // comes from the access token, so the membership rule sees the real caller.
        (_, Guid boardId) = await SeedBoardAsync();
        await AuthenticateAsNewUserAsync();
        var request = new CreateTaskRequest(boardId, "Valid title", null, Priority.Medium, null, null);

        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/v1/tasks", request);

        await AssertValidationProblemAsync(response, "RequestedByUserId", "Member or Admin");
    }

    [Fact]
    public async Task GetById_ExistingTask_ReturnsTask()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Investigate outage");

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.NotNull(task);
        Assert.Equal(taskId, task!.Id);
        Assert.Equal("Investigate outage", task.Title);
        Assert.Empty(task.Comments);
        Assert.Empty(task.Attachments);
    }

    [Fact]
    public async Task GetById_ExistingTask_LinksIncludeSelfAssignDeleteAndValidTransitions()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Investigate outage");

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.NotNull(task);
        // A freshly-created task starts in Backlog, which allows ToDo and Cancelled.
        Assert.Equal(ProjectTaskStatus.Backlog, task!.Status);
        Assert.Contains(task.Links, l => l.Rel == "self" && l.Href == $"/api/v1/tasks/{taskId}" && l.Method == "GET");
        Assert.Contains(task.Links, l => l.Rel == "assign" && l.Href == $"/api/v1/tasks/{taskId}/assign" && l.Method == "POST");
        Assert.Contains(task.Links, l => l.Rel == "delete" && l.Href == $"/api/v1/tasks/{taskId}" && l.Method == "DELETE");
        Assert.Contains(task.Links, l => l.Rel == "move" && l.Href == $"/api/v1/tasks/{taskId}/move" && l.Method == "POST");
        Assert.Contains(task.Links, l => l.Rel == "move:todo" && l.Href == $"/api/v1/tasks/{taskId}/move" && l.Method == "POST");
        Assert.Contains(task.Links, l => l.Rel == "move:cancelled" && l.Href == $"/api/v1/tasks/{taskId}/move" && l.Method == "POST");
    }

    [Fact]
    public async Task GetById_CancelledTask_LinksExcludeMove()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Abandoned task");
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);
        await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", ifMatch, new MoveTaskRequest(ProjectTaskStatus.Cancelled));

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.NotNull(task);
        Assert.Equal(ProjectTaskStatus.Cancelled, task!.Status);
        Assert.DoesNotContain(task.Links, l => l.Rel == "move" || l.Rel.StartsWith("move:", StringComparison.Ordinal));
        Assert.Contains(task.Links, l => l.Rel == "self");
    }

    [Fact]
    public async Task GetById_ExistingTask_ReturnsETagHeaderMatchingConcurrencyToken()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{taskId}");

        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.NotNull(task);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal($"\"{task!.ConcurrencyToken}\"", response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task GetById_NonexistentTask_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_FilterByBoardId_ReturnsOnlyMatchingTasksAndSetsTotalCountHeader()
    {
        // Seeded sequentially: SeedBoardAsync authenticates Client as the seeded user, and
        // HttpRequestHeaders is not thread-safe for concurrent writes.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        (Guid otherUserId, Guid otherBoardId) = await SeedBoardAsync();

        await Task.WhenAll(
            SeedTaskAsync(boardId, userId, "In scope"),
            SeedTaskAsync(otherBoardId, otherUserId, "Out of scope"));

        await AuthenticateAsUserAsync(userId); // we are looking for tasks in the first board, so we need to be a member of that project

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks?boardId={boardId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        Assert.NotNull(tasks);
        Assert.Single(tasks!);
        Assert.Equal("In scope", tasks![0].Title);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetTasks_TaskWithCommentsAndAttachments_ReportsLiveCountsAndAssigneeName()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        await SeedAsync(async db =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ProjectTask task = ProjectTask.Create(boardId, userId, "Has children", now);
            task.Assign(userId, userId, now);
            task.AddComment("Kept", userId, now);
            Comment retracted = task.AddComment("Retracted", userId, now);
            task.RemoveComment(retracted.Id, userId, requesterIsModerator: false, now);
            task.AddAttachment("spec.pdf", "application/pdf", 1_024, "/attachments/spec.pdf", userId, now);
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return task.Id;
        });
        await AuthenticateAsUserAsync(userId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/tasks?boardId={boardId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        TaskSummaryDto only = Assert.Single(tasks!);
        Assert.Equal(1, only.CommentCount); // soft-deleted comment excluded
        Assert.Equal(1, only.AttachmentCount);
        Assert.Equal(userId, only.AssigneeId);
        Assert.False(string.IsNullOrEmpty(only.AssigneeDisplayName));
    }

    [Theory]
    [InlineData(false, new[] { "Due first", "Due second", "Due third" })]
    [InlineData(true, new[] { "Due third", "Due second", "Due first" })]
    public async Task GetTasks_SortByDueDate_OrdersByDueDateNotCreation(bool descending, string[] expectedTitles)
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        // Created in an order that differs from due-date order in both directions.
        await SeedTaskWithDueDateAsync(boardId, userId, "Due second", days: 20);
        await SeedTaskWithDueDateAsync(boardId, userId, "Due third", days: 30);
        await SeedTaskWithDueDateAsync(boardId, userId, "Due first", days: 10);
        await AuthenticateAsUserAsync(userId);

        HttpResponseMessage response = await Client.GetAsync(
            $"/api/v1/tasks?boardId={boardId}&sortBy=dueDate&sortDescending={descending}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<TaskSummaryDto>? tasks = await response.Content.ReadFromJsonAsync<List<TaskSummaryDto>>(ApiJson.Options);
        Assert.Equal(expectedTitles, tasks!.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task Update_ValidRequest_Returns204AndPersistsChanges()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId, "Original title");
        var request = new UpdateTaskRequest("Updated title", "New description", Priority.Critical, null);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
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
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AssertConcurrentRequestsConflictAsync(
            () => SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, new UpdateTaskRequest("Title from request 1", null, Priority.Medium, null)),
            () => SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, new UpdateTaskRequest("Title from request 2", null, Priority.Medium, null)));
    }

    [Fact]
    public async Task Update_NonexistentTask_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        var request = new UpdateTaskRequest("Title", null, Priority.Medium, null);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{Guid.CreateVersion7()}", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_MissingIfMatch_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest("Valid title", null, Priority.Medium, null);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", null, request);

        await AssertValidationProblemAsync(response, "IfMatch");
    }

    [Fact]
    public async Task Update_StaleIfMatch_Returns409()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest("Valid title", null, Priority.Medium, null);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_EmptyTitle_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest(string.Empty, null, Priority.Medium, null);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_TitleTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest(new string('A', 201), null, Priority.Medium, null);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, request);

        await AssertValidationProblemAsync(response, "Title");
    }

    [Fact]
    public async Task Update_InvalidPriority_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new UpdateTaskRequest("Valid title", null, (Priority)9999, null);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, request);

        await AssertValidationProblemAsync(response, "Priority");
    }

    [Fact]
    public async Task Delete_ExistingTask_Returns204AndSoftDeletesTask()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"/api/v1/tasks/{taskId}", ifMatch);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        HttpResponseMessage getResponse = await Client.GetAsync($"/api/v1/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentTask_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await SendAsync(
            HttpMethod.Delete, $"/api/v1/tasks/{Guid.CreateVersion7()}", PlaceholderIfMatch);

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
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AssertConcurrentRequestsConflictAsync(
            () => SendAsync(HttpMethod.Delete, $"/api/v1/tasks/{taskId}", ifMatch),
            () => SendAsync(HttpMethod.Put, $"/api/v1/tasks/{taskId}", ifMatch, new UpdateTaskRequest("Title from request 2", null, Priority.Medium, null)));
    }

    [Fact]
    public async Task Move_ValidTransition_Returns204AndPersistsStatus()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new MoveTaskRequest(ProjectTaskStatus.ToDo);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", ifMatch, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.Equal(ProjectTaskStatus.ToDo, task!.Status);
    }

    [Fact]
    public async Task Move_InvalidTransition_Returns422()
    {
        // Backlog cannot jump straight to Done - only InReview -> Done is legal.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new MoveTaskRequest(ProjectTaskStatus.Done);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", ifMatch, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Move_NonexistentTask_Returns404()
    {
        var request = new MoveTaskRequest(ProjectTaskStatus.ToDo);

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{Guid.CreateVersion7()}/move", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Move_MissingIfMatch_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new MoveTaskRequest(ProjectTaskStatus.ToDo);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", null, request);

        await AssertValidationProblemAsync(response, "IfMatch");
    }

    [Fact]
    public async Task Move_StaleIfMatch_Returns409()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new MoveTaskRequest(ProjectTaskStatus.ToDo);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Move_ConcurrentModification_OneRequestReturns409()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        await AssertConcurrentRequestsConflictAsync(
            () => SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", ifMatch, new MoveTaskRequest(ProjectTaskStatus.ToDo)),
            () => SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/move", ifMatch, new MoveTaskRequest(ProjectTaskStatus.Cancelled)));
    }

    [Fact]
    public async Task Assign_ValidRequest_Returns204AndPersistsAssignee()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AssignTaskRequest(userId);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/assign", ifMatch, request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.Equal(userId, task!.AssigneeId);
    }

    [Fact]
    public async Task Assign_NonexistentAssignee_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AssignTaskRequest(Guid.CreateVersion7());
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/assign", ifMatch, request);

        await AssertValidationProblemAsync(response, "AssigneeId");
    }

    [Fact]
    public async Task Assign_NonexistentTask_Returns404()
    {
        (Guid userId, _) = await SeedBoardAsync();
        var request = new AssignTaskRequest(userId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{Guid.CreateVersion7()}/assign", PlaceholderIfMatch, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unassign_AssignedTask_Returns204AndClearsAssignee()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        byte[]? ifMatchBeforeAssign = await GetTaskRowVersionAsync(taskId);
        await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/assign", ifMatchBeforeAssign, new AssignTaskRequest(userId));
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/unassign", ifMatch);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.Null(task!.AssigneeId);
    }

    [Fact]
    public async Task Unassign_AlreadyUnassignedTask_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/unassign", ifMatch);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Unassign_NonexistentTask_Returns404()
    {

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{Guid.CreateVersion7()}/unassign", PlaceholderIfMatch);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Close_TaskInReview_Returns204AndSetsStatusDone()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        await MoveTaskAsync(taskId, userId, ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/close", ifMatch);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.Equal(ProjectTaskStatus.Done, task!.Status);
    }

    [Fact]
    public async Task Close_TaskNotInReview_Returns422()
    {
        // A freshly created task starts in Backlog - Backlog -> Done is not a legal transition.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/close", ifMatch);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Close_NonexistentTask_Returns404()
    {

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{Guid.CreateVersion7()}/close", PlaceholderIfMatch);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reopen_DoneTask_Returns204AndSetsStatusToDo()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        await MoveTaskAsync(taskId, userId, ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview, ProjectTaskStatus.Done);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/reopen", ifMatch);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        TaskDto? task = await (await Client.GetAsync($"/api/v1/tasks/{taskId}")).Content.ReadFromJsonAsync<TaskDto>(ApiJson.Options);
        Assert.Equal(ProjectTaskStatus.ToDo, task!.Status);
    }

    [Fact]
    public async Task Reopen_TaskNotDone_Returns422()
    {
        // Reopen is Done -> ToDo only. Backlog -> ToDo happens to be a legal transition in its
        // own right, so this needs a status that cannot reach ToDo at all - InReview's allowed
        // transitions are Done/InProgress/Cancelled, not ToDo.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        await MoveTaskAsync(taskId, userId, ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview);
        byte[]? ifMatch = await GetTaskRowVersionAsync(taskId);

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/reopen", ifMatch);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Reopen_NonexistentTask_Returns404()
    {

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{Guid.CreateVersion7()}/reopen", PlaceholderIfMatch);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_ValidRequest_Returns201WithLocation()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AddCommentRequest("Looks good to me");

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CommentDto? comment = await response.Content.ReadFromJsonAsync<CommentDto>(ApiJson.Options);
        Assert.NotNull(comment);
        Assert.Equal("Looks good to me", comment!.Content);
        Assert.Equal($"/api/v1/tasks/{taskId}/comments", response.Headers.Location?.PathAndQuery);
        // The author is the authenticated caller - the request body carries no user ID at all.
        Assert.Equal(userId, comment.AuthorId);
    }

    [Fact]
    public async Task AddComment_NonexistentTask_Returns404()
    {
        var request = new AddCommentRequest("Comment on nothing");

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{Guid.CreateVersion7()}/comments", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_EmptyContent_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AddCommentRequest(string.Empty);

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments/", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AddComment_ContentTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new AddCommentRequest(new string('A', 10_001));

        HttpResponseMessage response = await Client.PostAsJsonAsync($"/api/v1/tasks/{taskId}/comments/", request);

        await AssertValidationProblemAsync(response, "Content");
    }

    [Fact]
    public async Task EditComment_ValidRequest_Returns204AndPersistsChanges()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        var request = new EditCommentRequest("Edited comment");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<CommentDto>? comments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/comments")).Content.ReadFromJsonAsync<List<CommentDto>>(ApiJson.Options);
        CommentDto? comment = comments?.FirstOrDefault(c => c.Id == commentId);
        Assert.NotNull(comment);
        Assert.Equal("Edited comment", comment!.Content);
    }

    [Fact]
    public async Task EditComment_NonExistingTask_Returns404()
    {
        var request = new EditCommentRequest("Edited comment");

        await AuthenticateAsNewUserAsync(); // we need to be authenticated to get a 404 instead of a 401

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{Guid.CreateVersion7()}/comments/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditComment_NonexistentComment_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        var request = new EditCommentRequest("Edited comment");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{Guid.CreateVersion7()}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EditComment_EmptyNewContent_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        var request = new EditCommentRequest(string.Empty);

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EditComment_ContentTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        var request = new EditCommentRequest(new string('A', 10_001));

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        await AssertValidationProblemAsync(response, "NewContent");
    }

    [Fact]
    public async Task EditComment_NonAuthorEditsSomeonesComment_Returns422()
    {
        // The combined task/comment/author MustAsync rule has no OverridePropertyName, so
        // FluentValidation keys it under the empty string "" - the same rule the
        // NonExistingTask/NonexistentComment tests above already trip, just via a different input.
        // The non-author must be a project Member to get past the TaskContributor policy (a
        // non-member would get 404 before the validator runs), and is authenticated as
        // themselves - the request body no longer carries a user ID that could claim authorship.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Initial comment");
        Guid nonAuthorUserId = await SeedProjectMemberAsync(boardId, Role.Member, "Bob");
        await AuthenticateAsUserAsync(nonAuthorUserId);
        var request = new EditCommentRequest("Edited by someone else");

        HttpResponseMessage response = await Client.PutAsJsonAsync($"/api/v1/tasks/{taskId}/comments/{commentId}", request);

        await AssertValidationProblemAsync(response, string.Empty);
    }

    [Fact]
    public async Task RemoveComment_ExistingComment_Returns204AndRemovesComment()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid commentId = await SeedCommentAsync(taskId, userId, "Delete me");

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}/comments/{commentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<CommentDto>? comments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/comments")).Content.ReadFromJsonAsync<List<CommentDto>>(ApiJson.Options);
        Assert.DoesNotContain(comments!, c => c.Id == commentId);
    }

    [Fact]
    public async Task RemoveComment_NonAuthorProjectMember_Returns422AndKeepsComment()
    {
        // TaskContributor only proves the caller is a project Member; removing someone else's
        // comment additionally needs project Admin (or org Admin), enforced by the aggregate.
        (Guid adminUserId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, adminUserId);
        Guid commentId = await SeedCommentAsync(taskId, adminUserId, "Admin's comment");
        Guid memberUserId = await SeedProjectMemberAsync(boardId, Role.Member, "Bob");
        await AuthenticateAsUserAsync(memberUserId);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}/comments/{commentId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ApiJson.Options);
        Assert.Equal("urn:ordinis:error:task.comment-not-author", problem?.Type);

        List<CommentDto>? comments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/comments")).Content.ReadFromJsonAsync<List<CommentDto>>(ApiJson.Options);
        Assert.Contains(comments!, c => c.Id == commentId);
    }

    [Fact]
    public async Task RemoveComment_ProjectAdminRemovesAnotherUsersComment_Returns204()
    {
        (Guid adminUserId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, adminUserId);
        Guid memberUserId = await SeedProjectMemberAsync(boardId, Role.Member, "Bob");
        Guid commentId = await SeedCommentAsync(taskId, memberUserId, "Member's comment");

        // Client is still authenticated as the project Admin from SeedBoardAsync.
        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}/comments/{commentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<CommentDto>? comments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/comments")).Content.ReadFromJsonAsync<List<CommentDto>>(ApiJson.Options);
        Assert.DoesNotContain(comments!, c => c.Id == commentId);
    }

    [Fact]
    public async Task RemoveComment_NonexistentTask_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/v1/tasks/{Guid.CreateVersion7()}/comments/{Guid.CreateVersion7()}");

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
            $"/api/v1/tasks/{taskId}/comments/{Guid.CreateVersion7()}");

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

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AttachmentDto? attachment = await response.Content.ReadFromJsonAsync<AttachmentDto>(ApiJson.Options);
        Assert.NotNull(attachment);
        Assert.Equal("notes.txt", attachment!.FileName);
        Assert.NotEmpty(attachment.DownloadUrl);
        Assert.Equal(userId, attachment.UploadedByUserId);

        // Clean up the file LocalFileStorageService wrote to disk - Respawn only resets the database.
        await Client.DeleteAsync($"/api/v1/tasks/{taskId}/attachments/{attachment.Id}");
    }

    [Fact]
    public async Task AddAttachment_NonexistentTask_Returns404()
    {
        // The uploader is the authenticated caller, who must be a real, existing user -
        // AddAttachmentValidator checks that existence itself (422 on failure) - so this seeds a
        // real user rather than authenticating with an arbitrary token.
        await SeedBoardAsync();

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "notes.txt");

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
    public async Task AddAttachment_FileNameAtMaximumLength_Returns201AndKeepsTheFullName()
    {
        // 255 characters is valid per AddAttachmentValidator. The stored file is named
        // "{guid}_{name}", which would exceed the filesystem's 255-character limit per path
        // component - LocalFileStorageService must shorten the stored name, not fail with a 500.
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        string fileName = new string('a', 251) + ".txt";

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", fileName);

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AttachmentDto? attachment = await response.Content.ReadFromJsonAsync<AttachmentDto>(ApiJson.Options);
        Assert.NotNull(attachment);
        Assert.Equal(fileName, attachment!.FileName);
        Assert.EndsWith(".txt", attachment.DownloadUrl);

        // Clean up the file LocalFileStorageService wrote to disk - Respawn only resets the database.
        await Client.DeleteAsync($"/api/v1/tasks/{taskId}/attachments/{attachment.Id}");
    }

    [Fact]
    public async Task AddAttachment_FileNameTooLong_Returns422()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", new string('a', 256) + ".txt");

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

        HttpResponseMessage response = await Client.PostAsync($"/api/v1/tasks/{taskId}/attachments", content);

        await AssertValidationProblemAsync(response, "SizeInBytes");
    }


    [Fact]
    public async Task RemoveAttachment_ExistingAttachment_Returns204AndRemovesAttachment()
    {
        (Guid userId, Guid boardId) = await SeedBoardAsync();
        Guid taskId = await SeedTaskAsync(boardId, userId);
        Guid attachmentId = await SeedAttachmentAsync(taskId, userId);

        HttpResponseMessage response = await Client.DeleteAsync($"/api/v1/tasks/{taskId}/attachments/{attachmentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        List<AttachmentDto>? attachments = await (await Client.GetAsync($"/api/v1/tasks/{taskId}/attachments")).Content.ReadFromJsonAsync<List<AttachmentDto>>(ApiJson.Options);
        Assert.DoesNotContain(attachments!, a => a.Id == attachmentId);
    }

    [Fact]
    public async Task RemoveAttachment_NonexistentTask_Returns404()
    {
        await AuthenticateAsNewUserAsync();

        HttpResponseMessage response = await Client.DeleteAsync(
            $"/api/v1/tasks/{Guid.CreateVersion7()}/attachments/{Guid.CreateVersion7()}");

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
            $"/api/v1/tasks/{taskId}/attachments/{Guid.CreateVersion7()}");

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

        await AuthenticateAsUserAsync(userId);

        return (userId, boardId);
    }

    /// <summary>
    /// Seeds a new user in the same organization as <paramref name="boardId"/>'s project and adds
    /// them to that project with <paramref name="role"/>. Does not change who <c>Client</c> is
    /// authenticated as.
    /// </summary>
    private Task<Guid> SeedProjectMemberAsync(Guid boardId, Role role, string displayName) => SeedAsync(async db =>
    {
        Guid projectId = await db.Boards.Where(b => b.Id == boardId).Select(b => b.ProjectId).SingleAsync();
        Project project = await db.Projects.Include(p => p.Members).SingleAsync(p => p.Id == projectId);

        User user = User.Create(
            project.OrganizationId,
            displayName,
            $"{displayName.ToLowerInvariant()}-{Guid.CreateVersion7()}@example.com",
            "hashed-password",
            Role.Member);
        db.Users.Add(user);

        project.AddMember(user.Id, role, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        return user.Id;
    });

    private Task<Guid> SeedTaskAsync(Guid boardId, Guid reporterId, string title = "Default task") => SeedAsync(async db =>
    {
        ProjectTask task = ProjectTask.Create(boardId, reporterId, title, DateTimeOffset.UtcNow);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return task.Id;
    });

    private Task<Guid> SeedTaskWithDueDateAsync(Guid boardId, Guid reporterId, string title, int days) => SeedAsync(async db =>
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProjectTask task = ProjectTask.Create(boardId, reporterId, title, now, dueDate: now.AddDays(days));
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return task.Id;
    });

    /// <summary>
    /// Walks a task through a sequence of legal transitions directly via the domain, bypassing
    /// HTTP - used to put a task into a specific status (e.g. InReview, Done) as test setup
    /// without that setup itself depending on the <c>/move</c> endpoint under test.
    /// </summary>
    private Task<bool> MoveTaskAsync(Guid taskId, Guid movedByUserId, params ProjectTaskStatus[] path) => SeedAsync(async db =>
    {
        var task = await db.Tasks.FindAsync(taskId);
        foreach (ProjectTaskStatus status in path)
        {
            task!.Move(status, movedByUserId, DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();

        return true;
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
