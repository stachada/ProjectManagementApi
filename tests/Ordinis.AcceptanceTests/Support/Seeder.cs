using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// Arranges "Given" state directly in the database through the real domain model (so every
/// invariant still holds) instead of through the API — faster, and independent of the
/// endpoints a scenario is actually testing.
/// </summary>
internal sealed class Seeder(World world)
{
    /// <summary>Password every seeded persona can sign in with.</summary>
    public const string DefaultPassword = "Passw0rd!-acceptance";

    /// <summary>
    /// The real hasher's output for <see cref="DefaultPassword"/>, computed once per run: hashing
    /// is deliberately slow (BCrypt), and a scenario seeds several users. A salted hash stays
    /// valid for every user it is assigned to, so sign-in still goes through real verification.
    /// </summary>
    private static readonly Lazy<string> DefaultPasswordHash = new(() =>
    {
        using IServiceScope scope = TestHost.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(DefaultPassword);
    });

    /// <summary>
    /// The shortest legal path from Backlog to each status, used to put a seeded task into a
    /// given status without bypassing <see cref="ProjectTask.Move"/>'s transition rules.
    /// </summary>
    private static readonly Dictionary<ProjectTaskStatus, ProjectTaskStatus[]> PathFromBacklog = new()
    {
        [ProjectTaskStatus.Backlog] = [],
        [ProjectTaskStatus.ToDo] = [ProjectTaskStatus.ToDo],
        [ProjectTaskStatus.InProgress] = [ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress],
        [ProjectTaskStatus.InReview] = [ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview],
        [ProjectTaskStatus.Done] = [ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview, ProjectTaskStatus.Done],
        [ProjectTaskStatus.Cancelled] = [ProjectTaskStatus.Cancelled],
    };

    public async Task<Guid> CreateOrganizationAsync(string name)
    {
        Guid id = await WithDbAsync(async db =>
        {
            Organization organization = Organization.Create(name, Slugify(name));
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();
            return organization.Id;
        });

        world.Organizations[name] = id;
        return id;
    }

    public async Task<World.Persona> CreatePersonaAsync(string name, Guid organizationId, Role orgRole)
    {
        Guid userId = await CreateUserAsync(organizationId, name, orgRole);
        var persona = new World.Persona(name, userId, organizationId);
        world.Personas[name] = persona;
        return persona;
    }

    public async Task<Guid> CreateProjectWithBoardAsync(Guid organizationId, string projectName, string boardName)
    {
        Guid founderId = await CreateUserAsync(organizationId, $"Founder of {projectName}", Role.Member);

        (Guid projectId, Guid boardId) = await WithDbAsync(async db =>
        {
            DateTimeOffset now = Now(db);
            Project project = Project.Create(organizationId, founderId, projectName, Slugify(projectName), now);
            Board board = Board.Create(project.Id, boardName, founderId);
            db.Projects.Add(project);
            db.Boards.Add(board);
            await db.SaveChangesAsync();
            return (project.Id, board.Id);
        });

        world.Projects[projectName] = projectId;
        world.Boards[boardName] = boardId;
        world.ProjectFounders[projectId] = founderId;
        world.ProjectOrganizations[projectId] = organizationId;
        world.BoardProjects[boardId] = projectId;
        return projectId;
    }

    /// <summary>
    /// Adds a member; the first Admin added replaces the hidden founder, so the project's member
    /// list contains exactly the personas the scenario names.
    /// </summary>
    public Task AddProjectMemberAsync(Guid projectId, Guid userId, Role role) => WithDbAsync(async db =>
    {
        Project project = await db.Projects.Include(p => p.Members).SingleAsync(p => p.Id == projectId);
        project.AddMember(userId, role, Now(db));

        if (role == Role.Admin && world.ProjectFounders.Remove(projectId, out Guid founderId))
        {
            project.RemoveMember(founderId);
        }

        await db.SaveChangesAsync();
        return true;
    });

    public async Task<Guid> CreateTaskAsync(Guid boardId, string title)
    {
        Guid projectId = world.BoardProjects[boardId];
        Guid reporterId = await AnyProjectUserAsync(projectId);

        Guid taskId = await WithDbAsync(async db =>
        {
            ProjectTask task = ProjectTask.Create(boardId, reporterId, title, Now(db));
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return task.Id;
        });

        world.Tasks[title] = taskId;
        return taskId;
    }

    public Task SetTaskStatusAsync(Guid taskId, ProjectTaskStatus target) => WithDbAsync(async db =>
    {
        ProjectTask task = await db.Tasks.SingleAsync(t => t.Id == taskId);
        if (task.Status != ProjectTaskStatus.Backlog)
        {
            throw new InvalidOperationException(
                $"Seeding a status assumes the task is still in Backlog, but it is {task.Status}.");
        }

        Guid actorId = task.ReporterId;
        foreach (ProjectTaskStatus step in PathFromBacklog[target])
        {
            task.Move(step, actorId, Now(db));
        }

        await db.SaveChangesAsync();
        return true;
    });

    public Task AssignTaskAsync(Guid taskId, Guid assigneeId) => WithDbAsync(async db =>
    {
        ProjectTask task = await db.Tasks.SingleAsync(t => t.Id == taskId);
        task.Assign(assigneeId, task.ReporterId, Now(db));
        await db.SaveChangesAsync();
        return true;
    });

    public Task RegisterWebhookAsync(Guid projectId, string url, IReadOnlyList<string> eventTypes) => WithDbAsync(async db =>
    {
        Project project = await db.Projects.Include(p => p.Members).Include(p => p.Webhooks).SingleAsync(p => p.Id == projectId);
        Guid registeredBy = project.Members.First(m => m.Role == Role.Admin).UserId;
        project.RegisterWebhook(url, eventTypes, registeredBy, Now(db));
        await db.SaveChangesAsync();
        return true;
    });

    /// <summary>Reads a task as stored (soft-deleted tasks are hidden by the query filter).</summary>
    public Task<ProjectTask?> FindTaskAsync(Guid taskId) =>
        WithDbAsync(db => db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId));

    /// <summary>The current version of a task, formatted exactly like the API's ETag.</summary>
    public async Task<string> CurrentTaskVersionAsync(Guid taskId)
    {
        byte[]? rowVersion = await WithDbAsync(db =>
            db.Tasks.Where(t => t.Id == taskId).Select(t => t.RowVersion).SingleAsync());
        return $"\"{Convert.ToBase64String(rowVersion ?? [])}\"";
    }

    /// <summary>Issues a real access token for a user, signed exactly as the API signs them.</summary>
    public async Task<string> AccessTokenForAsync(Guid userId)
    {
        using IServiceScope scope = TestHost.Factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        User user = await db.Users.SingleAsync(u => u.Id == userId);
        IJwtTokenService tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        TimeProvider time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        return tokens.GenerateAccessToken(user, time.GetUtcNow()).AccessToken;
    }

    /// <summary>
    /// A user who can read everything in a project's organization — a hidden org Admin, created
    /// on first use — for read-back assertions that must not depend on any persona's access.
    /// </summary>
    public async Task<Guid> ObserverForOrganizationAsync(Guid organizationId)
    {
        string key = $"__observer:{organizationId}";
        if (world.Personas.TryGetValue(key, out World.Persona? observer))
        {
            return observer.UserId;
        }

        Guid userId = await CreateUserAsync(organizationId, "Observer", Role.Admin);
        world.Personas[key] = new World.Persona(key, userId, organizationId);
        return userId;
    }

    private async Task<Guid> AnyProjectUserAsync(Guid projectId) =>
        await WithDbAsync(db => db.Projects
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Members)
            .OrderByDescending(m => m.Role)
            .Select(m => m.UserId)
            .FirstAsync());

    private async Task<Guid> CreateUserAsync(Guid organizationId, string displayName, Role orgRole)
    {
        string orgSlug = await WithDbAsync(db =>
            db.Organizations.Where(o => o.Id == organizationId).Select(o => o.Slug).SingleAsync());

        return await WithDbAsync(async db =>
        {
            string email = $"{Slugify(displayName)}@{orgSlug}.example";
            User user = User.Create(organizationId, displayName, email, DefaultPasswordHash.Value, orgRole);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });
    }

    /// <summary>
    /// "Now" from the API's own <see cref="TimeProvider"/> singleton — the same clock the API
    /// stamps changes with, so seeded and API-made changes order consistently.
    /// </summary>
    private static DateTimeOffset Now(AppDbContext _) =>
        TestHost.Factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private static string Slugify(string name) =>
        string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using IServiceScope scope = TestHost.Factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await work(db);
    }
}
