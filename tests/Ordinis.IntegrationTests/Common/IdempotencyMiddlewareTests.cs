using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Ordinis.Api.Organizations.Requests;
using Ordinis.Api.Tasks.Requests;
using Ordinis.Application.Organizations.Dtos;
using Ordinis.Application.Tasks.Dtos;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;
using Ordinis.Testing;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Exercises <c>IdempotencyMiddleware</c> end-to-end against <c>POST /api/v1/organizations</c> —
/// one of the eight <c>[Idempotent]</c>-marked routes, chosen because it needs no parent-entity
/// seeding. See <c>docs/API_INFRASTRUCTURE.md#idempotencymiddleware</c> for the full mechanism.
/// </summary>
public sealed class IdempotencyMiddlewareTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Post_SameKeySameBody_ReplaysIdenticalResponseAndCreatesOnlyOneResource()
    {
        await AuthenticateAsOrgAdminAsync();
        string idempotencyKey = Guid.CreateVersion7().ToString();
        var request = new CreateOrganizationRequest("Replay Co", "Created once, replayed once.");

        HttpResponseMessage first = await SendWithIdempotencyKeyAsync(request, idempotencyKey);
        HttpResponseMessage second = await SendWithIdempotencyKeyAsync(request, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        OrganizationDto? firstDto = await first.Content.ReadFromJsonAsync<OrganizationDto>(ApiJson.Options);
        OrganizationDto? secondDto = await second.Content.ReadFromJsonAsync<OrganizationDto>(ApiJson.Options);
        Assert.NotNull(firstDto);
        Assert.NotNull(secondDto);
        Assert.Equal(firstDto.Id, secondDto.Id);

        int count = await SeedAsync(db => db.Organizations.CountAsync(o => o.Name == "Replay Co"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Post_SameKeyDifferentBody_Returns409Conflict()
    {
        await AuthenticateAsOrgAdminAsync();
        string idempotencyKey = Guid.CreateVersion7().ToString();

        HttpResponseMessage first = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest("Conflict Co A", "First body."), idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        HttpResponseMessage second = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest("Conflict Co B", "Different body, same key."), idempotencyKey);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_NoIdempotencyKey_CreatesTwoIndependentResources()
    {
        await AuthenticateAsOrgAdminAsync();
        var request = new CreateOrganizationRequest("No Key Co", "No idempotency key sent.");

        HttpResponseMessage first = await Client.PostAsJsonAsync("/api/v1/organizations", request);
        HttpResponseMessage second = await Client.PostAsJsonAsync("/api/v1/organizations", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // No Idempotency-Key means no replay guard, but CreateOrganizationValidator still rejects
        // a duplicate slug derived from the same Name - proves this is the *validator* rejecting
        // it (422), not the idempotency middleware replaying or conflicting (201/409).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task Post_FailedRequestThenRetryWithSameKey_SecondCallSucceeds()
    {
        await AuthenticateAsOrgAdminAsync();
        string idempotencyKey = Guid.CreateVersion7().ToString();

        HttpResponseMessage failed = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest(string.Empty, "Invalid - empty name."), idempotencyKey);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);

        HttpResponseMessage retried = await SendWithIdempotencyKeyAsync(
            new CreateOrganizationRequest("Retried Co", "Valid on retry, same key."), idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }

    [Fact]
    public async Task Post_SameKeySameBodyDifferentUser_IsNotReplayed()
    {
        // Arrange: user A creates an organization with in idempotency key
        await AuthenticateAsOrgAdminAsync();
        string idempotencyKey = Guid.CreateVersion7().ToString();
        var request = new CreateOrganizationRequest("User A Co", "Created by user A.");

        HttpResponseMessage first = await SendWithIdempotencyKeyAsync(request, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Act: a different user (a second org admin) sends the same key and the same body
        await AuthenticateAsOrgAdminAsync(); // re-authenticate as a different user
        HttpResponseMessage second = await SendWithIdempotencyKeyAsync(request, idempotencyKey);

        // Assert: B's request actually ran and hit CreateOrganizationValidator's global slug-uniqueness
        // rule. A replay would have returned A's cached 201 without running the validator at all.
        await AssertValidationProblemAsync(second, "Name");
        Assert.Null(second.Headers.Location);

        int count = await SeedAsync(db => db.Organizations.CountAsync(o => o.Name == "User A Co"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Post_SameKeySameBodyDifferentUser_IsNotReplayedAndCreatesSecondResource()
    {
        // Arrange: a task whose project has two contributors: A (creator, Admin) and B (Member)
        (Guid taskId, Guid userAId, Guid userBId) = await SeedTaskWithTwoContributorsAsync();
        string path = $"/api/v1/tasks/{taskId}/comments";
        string idempotencyKey = Guid.CreateVersion7().ToString();
        var request = new AddCommentRequest("Same comment, same key");

        await AuthenticateAsUserAsync(userAId);
        HttpResponseMessage first = await SendWithIdempotencyKeyAsync(path, request, idempotencyKey);

        // Act: B sends the identical key and body
        await AuthenticateAsUserAsync(userBId);
        HttpResponseMessage second = await SendWithIdempotencyKeyAsync(path, request, idempotencyKey);

        // Assert: both requests executed, giving two distinct comments rather than one replayed response
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        CommentDto? firstComment = await first.Content.ReadFromJsonAsync<CommentDto>(ApiJson.Options);
        CommentDto? secondComment = await second.Content.ReadFromJsonAsync<CommentDto>(ApiJson.Options);
        Assert.NotNull(firstComment);
        Assert.NotNull(secondComment);
        Assert.NotEqual(firstComment.Id, secondComment.Id);
        // Each comment is authored by whoever was authenticated when it was sent.
        Assert.Equal(userAId, firstComment.AuthorId);
        Assert.Equal(userBId, secondComment.AuthorId);

        int commentCount = await SeedAsync(db => db.Tasks
            .Where(t => t.Id == taskId)
            .SelectMany(t => t.Comments)
            .CountAsync());
        Assert.Equal(2, commentCount);
    }

    /// <summary>
    /// A retried upload is the same request even though the client picks a new multipart boundary
    /// for it (HttpClient does, like most clients): it replays instead of conflicting.
    /// </summary>
    [Fact]
    public async Task Post_RetriedUploadWithNewMultipartBoundary_ReplaysAndCreatesOneAttachment()
    {
        (Guid taskId, Guid userAId, _) = await SeedTaskWithTwoContributorsAsync();
        await AuthenticateAsUserAsync(userAId);
        string idempotencyKey = Guid.CreateVersion7().ToString();

        HttpResponseMessage first = await UploadWithIdempotencyKeyAsync(taskId, idempotencyKey, boundary: "first-attempt");
        HttpResponseMessage retry = await UploadWithIdempotencyKeyAsync(taskId, idempotencyKey, boundary: "retried-attempt");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(first.Headers.Location, retry.Headers.Location);

        int attachments = await SeedAsync(db => db.Tasks.Where(t => t.Id == taskId).SelectMany(t => t.Attachments).CountAsync());
        Assert.Equal(1, attachments);
    }

    [Fact]
    public async Task Post_SameKeyDifferentUploadContent_Returns409Conflict()
    {
        (Guid taskId, Guid userAId, _) = await SeedTaskWithTwoContributorsAsync();
        await AuthenticateAsUserAsync(userAId);
        string idempotencyKey = Guid.CreateVersion7().ToString();

        HttpResponseMessage first = await UploadWithIdempotencyKeyAsync(taskId, idempotencyKey, boundary: "first-attempt");
        HttpResponseMessage other = await UploadWithIdempotencyKeyAsync(taskId, idempotencyKey, boundary: "first-attempt", content: "other log");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadWithIdempotencyKeyAsync(
        Guid taskId, string idempotencyKey, string boundary, string content = "crash log")
    {
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tasks/{taskId}/attachments")
        {
            Content = new MultipartFormDataContent(boundary) { { file, "file", "crash.log" } },
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendWithIdempotencyKeyAsync(CreateOrganizationRequest body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organizations")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return await Client.SendAsync(request);
    }

    private Task<(Guid TaskId, Guid UserAId, Guid UserBId)> SeedTaskWithTwoContributorsAsync() => SeedAsync(async db =>
    {
        Organization organization = CreateOrganization();
        db.Organizations.Add(organization);

        User userA = User.Create(organization.Id, "User A", "a@example.com", "hashed-password");
        User userB = User.Create(organization.Id, "User B", "b@example.com", "hashed-password");
        db.Users.AddRange(userA, userB);

        // A is auto-added as the project's Admin; B is added as a Member, so both pass TaskContributor.
        Project project = Project.Create(organization.Id, userA.Id, "Apollo", "apollo", DateTimeOffset.UtcNow);
        project.AddMember(userB.Id, Role.Member, DateTimeOffset.UtcNow);
        db.Projects.Add(project);

        Board board = Board.Create(project.Id, "Sprint Board", userA.Id);
        db.Boards.Add(board);

        ProjectTask task = ProjectTask.Create(board.Id, userA.Id, "Shared task", DateTimeOffset.UtcNow);
        db.Tasks.Add(task);

        await db.SaveChangesAsync();
        return (task.Id, userA.Id, userB.Id);
    });

    private async Task<HttpResponseMessage> SendWithIdempotencyKeyAsync<TBody>(
        string path,
        TBody body,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return await Client.SendAsync(request);
    }
}
