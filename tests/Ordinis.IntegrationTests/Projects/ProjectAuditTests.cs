using System.Net;
using System.Net.Http.Json;
using Ordinis.Application.Projects.Dtos;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Projects;

public sealed class ProjectAuditTests(
    OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task GetAudit_ProjectWithEvents_ReturnsEntriesNewestFirst()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid assigneeId = await SeedUserAsync(organizationId);
        (Guid projectId, Guid taskId) = await SeedProjectWithTaskEventsAsync(organizationId, userId, assigneeId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<AuditEntryDto>? entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntryDto>>();
        Assert.NotNull(entries);

        // Seed helper raises TaskCreated then TaskAssigned (see below) - newest-first means
        // TaskAssigned must come before TaskCreated in the response.
        //
        // Both events' ActorId are userId, NOT assigneeId: ProjectTask.Create(boardId,
        // reporterId, ...) sets TaskCreated.ReporterId = userId, and the seed helper calls
        // task.Assign(assigneeId, assignedByUserId: userId, now) - so TaskAssigned.AssignedByUserId
        // is also userId (the reporter performing the assignment), never the assignee being
        // assigned. assigneeId never appears as an actor anywhere in this test.
        Assert.Equal(2, entries.Count);
        Assert.Equal("TaskAssigned", entries[0].EventType);
        Assert.Equal("TaskCreated", entries[1].EventType);
        Assert.Equal(userId, entries[0].ActorId);
        Assert.Equal(userId, entries[1].ActorId);
        Assert.True(entries[0].OccurredAt >= entries[1].OccurredAt);
        Assert.Equal(taskId, entries[1].Payload.GetProperty("TaskId").GetGuid());

        Assert.True(response.Headers.Contains("X-Total-Count"));
        Assert.Equal("2", response.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetAudit_PageSizeSmallerThanTotalEvents_PagesCorrectly()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userId = await SeedUserAsync(organizationId);
        Guid assigneeId = await SeedUserAsync(organizationId);
        (Guid projectId, _) = await SeedProjectWithTaskEventsAsync(organizationId, userId, assigneeId);

        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{projectId}/audit?page=1&pageSize=1");

        IReadOnlyList<AuditEntryDto>? entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntryDto>>();
        Assert.NotNull(entries);
        Assert.Single(entries);
        Assert.Equal("TaskAssigned", entries[0].EventType); // newest-first, so page 1 is the latest event
        Assert.Equal("2", response.Headers.GetValues("X-Total-Count").Single()); // total, not page size
    }

    [Fact]
    public async Task GetAudit_EventsExistInAnotherProject_DoesNotLeakAcrossProjects()
    {
        Guid organizationId = await SeedOrganizationAsync();
        Guid userA = await SeedUserAsync(organizationId);
        Guid userB = await SeedUserAsync(organizationId);
        (Guid projectAId, _) = await SeedProjectWithTaskEventsAsync(organizationId, userA, userB);
        (Guid projectBId, _) = await SeedProjectWithTaskEventsAsync(organizationId, userB, userA);

        HttpResponseMessage responseA = await Client.GetAsync($"/api/v1/projects/{projectAId}/audit");

        IReadOnlyList<AuditEntryDto>? entriesA = await responseA.Content.ReadFromJsonAsync<IReadOnlyList<AuditEntryDto>>();
        Assert.NotNull(entriesA);
        // Project B's events must not appear - this is the real test of the Tasks/Boards join
        // actually filtering by ProjectId, which no unit test can exercise (GetDbConnection()
        // throws in TestAppDbContext).
        //
        // projectA was seeded with reporterId: userA, so BOTH of its events (TaskCreated.ReporterId
        // and TaskAssigned.AssignedByUserId) have ActorId == userA - userB is only over the
        // assignee (a target, not an actor) for ProjectA's events. If this assertion ever saw
        // userB, it would mean projectB's event leaked into projectA's response.
        Assert.Equal(2, entriesA.Count);
        Assert.All(entriesA, e => Assert.Equal(userA, e.ActorId));
    }

    [Fact]
    public async Task GetAudit_NonExistentProject_Returns404()
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/projects/{Guid.CreateVersion7()}/audit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        (Guid organizationId, _) = await SeedOrganizationWithUserAsync();
        return organizationId;
    }

    private Task<Guid> SeedUserAsync(Guid organizationId) => SeedAsync(async db =>
    {
        var email = $"bob-{Guid.CreateVersion7()}@example.com";
        var user = User.Create(organizationId, "Bob", email, "hashed-password");
        db.Users.Add(user);

        await db.SaveChangesAsync();
        return user.Id;
    });

    private async Task<(Guid ProjectId, Guid TaskId)> SeedProjectWithTaskEventsAsync(
        Guid organizationId,
        Guid reporterId,
        Guid assigneeId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return await SeedAsync(async db =>
        {
            Project project = Project.Create(
                organizationId,
                reporterId,
                "Apollo",
                $"apollo-{Guid.CreateVersion7()}",
                now);
            db.Projects.Add(project);

            Board board = Board.Create(
                project.Id,
                "Main Board",
                reporterId);
            db.Boards.Add(board);

            ProjectTask task = ProjectTask.Create(
                board.Id,
                reporterId,
                "Task 1",
                now);
            db.Tasks.Add(task);
            await db.SaveChangesAsync(); // flushes the TaskCreated OutboxMessage

            task.Assign(assigneeId, reporterId, now.AddMinutes(1));
            await db.SaveChangesAsync(); // flushes the TaskAssigned OutboxMessage

            return (project.Id, task.Id);
        });
    }
}
