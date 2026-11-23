using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Tasks.Requests;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Tasks;

/// <summary>
/// Regression for GAP-08: an archived board or project is read-only for its tasks. Every task
/// mutation endpoint — the task itself, its comments and its attachments — must answer
/// <c>422</c> with the container's error code, and change nothing; reads keep working.
/// </summary>
public sealed class ArchivedContainerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    /// <summary>Every task mutation endpoint, by name.</summary>
    public static TheoryData<string, string> ArchivedContainerAndMutation()
    {
        string[] mutations =
        [
            "create", "update", "delete", "move", "close", "reopen", "assign", "unassign",
            "add-comment", "edit-comment", "remove-comment", "add-attachment", "remove-attachment",
        ];

        var data = new TheoryData<string, string>();
        foreach (string container in new[] { "board", "project" })
        {
            foreach (string mutation in mutations)
            {
                data.Add(container, mutation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ArchivedContainerAndMutation))]
    public async Task TaskMutation_ContainerArchived_Returns422WithContainerErrorCode(string container, string mutation)
    {
        Seeded seeded = await SeedTaskWithCommentAndAttachmentAsync();
        await ArchiveAsync(container, seeded);
        byte[]? taskVersion = await GetTaskRowVersionAsync(seeded.TaskId);

        HttpResponseMessage response = await SendMutationAsync(mutation, seeded, taskVersion);

        if (mutation == "create" && container == "board")
        {
            // CreateTaskValidator already rejects an archived board as a field error (the
            // documented contract for this endpoint); every other case reaches TaskContainerGuard.
            await AssertValidationProblemAsync(response, "BoardId");
        }
        else
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ApiJson.Options);
            Assert.Equal($"urn:ordinis:error:{container}.archived", problem?.Type);
        }

        // Nothing changed: same version, same comment and attachment, no extra task.
        Assert.Equal(taskVersion, await GetTaskRowVersionAsync(seeded.TaskId));
        (int tasks, int comments, int attachments) = await SeedAsync(async db => (
            await db.Tasks.CountAsync(t => t.BoardId == seeded.BoardId),
            await db.Tasks.Where(t => t.Id == seeded.TaskId).SelectMany(t => t.Comments).CountAsync(),
            await db.Tasks.Where(t => t.Id == seeded.TaskId).SelectMany(t => t.Attachments).CountAsync()));
        Assert.Equal((1, 1, 1), (tasks, comments, attachments));
    }

    [Theory]
    [InlineData("board")]
    [InlineData("project")]
    public async Task TaskReads_ContainerArchived_StillSucceed(string container)
    {
        Seeded seeded = await SeedTaskWithCommentAndAttachmentAsync();
        await ArchiveAsync(container, seeded);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/v1/tasks/{seeded.TaskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/v1/tasks/{seeded.TaskId}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/v1/boards/{seeded.BoardId}/tasks")).StatusCode);
    }

    [Fact]
    public async Task TaskMutation_ContainerUnarchived_SucceedsAgain()
    {
        Seeded seeded = await SeedTaskWithCommentAndAttachmentAsync();
        await ArchiveAsync("board", seeded);
        await SeedAsync(async db =>
        {
            Board board = await db.Boards.SingleAsync(b => b.Id == seeded.BoardId);
            board.Unarchive();
            return await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await SendMutationAsync("move", seeded, await GetTaskRowVersionAsync(seeded.TaskId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record Seeded(Guid UserId, Guid ProjectId, Guid BoardId, Guid TaskId, Guid CommentId, Guid AttachmentId);

    /// <summary>
    /// A project (the caller is its Admin) with one board and one task in ToDo, assigned to the
    /// caller, with one comment and one attachment — so every mutation has a valid target and
    /// would succeed if the containers were not archived.
    /// </summary>
    private async Task<Seeded> SeedTaskWithCommentAndAttachmentAsync()
    {
        (Guid organizationId, Guid userId) = await SeedOrganizationWithUserAsync();

        Seeded seeded = await SeedAsync(async db =>
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Project project = Project.Create(organizationId, userId, "Apollo", "apollo", now);
            Board board = Board.Create(project.Id, "Sprint 1", userId);
            ProjectTask task = ProjectTask.Create(board.Id, userId, "Fix login bug", now);
            task.Move(ProjectTaskStatus.ToDo, userId, now);
            task.Assign(userId, userId, now);
            Comment comment = task.AddComment("On it", userId, now);
            Attachment attachment = task.AddAttachment("crash.log", "text/plain", 4, "test://crash.log", userId, now);

            db.Projects.Add(project);
            db.Boards.Add(board);
            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            return new Seeded(userId, project.Id, board.Id, task.Id, comment.Id, attachment.Id);
        });

        await AuthenticateAsUserAsync(userId);
        return seeded;
    }

    private Task ArchiveAsync(string container, Seeded seeded) => SeedAsync(async db =>
    {
        if (container == "board")
        {
            (await db.Boards.SingleAsync(b => b.Id == seeded.BoardId)).Archive();
        }
        else
        {
            (await db.Projects.SingleAsync(p => p.Id == seeded.ProjectId)).Archive();
        }

        return await db.SaveChangesAsync();
    });

    private async Task<HttpResponseMessage> SendMutationAsync(string mutation, Seeded seeded, byte[]? taskVersion)
    {
        string task = $"/api/v1/tasks/{seeded.TaskId}";

        switch (mutation)
        {
            case "create":
                return await Client.PostAsJsonAsync("/api/v1/tasks",
                    new CreateTaskRequest(seeded.BoardId, "Write docs", null, Priority.Medium, null, null));
            case "update":
                return await SendAsync(HttpMethod.Put, task, taskVersion,
                    new UpdateTaskRequest("Fix login bug (iOS)", null, Priority.High, null));
            case "delete":
                return await SendAsync(HttpMethod.Delete, task, taskVersion);
            case "move":
                return await SendAsync(HttpMethod.Post, $"{task}/move", taskVersion, new MoveTaskRequest(ProjectTaskStatus.InProgress));
            case "close":
                return await SendAsync(HttpMethod.Post, $"{task}/close", taskVersion);
            case "reopen":
                return await SendAsync(HttpMethod.Post, $"{task}/reopen", taskVersion);
            case "assign":
                return await SendAsync(HttpMethod.Post, $"{task}/assign", taskVersion, new AssignTaskRequest(seeded.UserId));
            case "unassign":
                return await SendAsync(HttpMethod.Post, $"{task}/unassign", taskVersion);
            case "add-comment":
                return await Client.PostAsJsonAsync($"{task}/comments", new AddCommentRequest("Still on it"));
            case "edit-comment":
                return await Client.PutAsJsonAsync($"{task}/comments/{seeded.CommentId}", new EditCommentRequest("Edited"));
            case "remove-comment":
                return await Client.DeleteAsync($"{task}/comments/{seeded.CommentId}");
            case "add-attachment":
                using (var content = new MultipartFormDataContent())
                {
                    var file = new ByteArrayContent([1, 2, 3, 4]);
                    file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                    content.Add(file, "file", "trace.log");
                    return await Client.PostAsync($"{task}/attachments", content);
                }
            case "remove-attachment":
                return await Client.DeleteAsync($"{task}/attachments/{seeded.AttachmentId}");
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.");
        }
    }
}
