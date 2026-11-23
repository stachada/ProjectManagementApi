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
    private static readonly Lazy<string> DefaultPasswordHash = new(() => HashPassword(DefaultPassword));

    private static string HashPassword(string password)
    {
        using IServiceScope scope = TestHost.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password);
    }

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

    /// <param name="slug">The organization's slug; derived from the name when omitted.</param>
    public async Task<Guid> CreateOrganizationAsync(string name, string? slug = null)
    {
        Guid id = await WithDbAsync(async db =>
        {
            Organization organization = Organization.Create(name, slug ?? Slugify(name));
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();
            return organization.Id;
        });

        world.Organizations[name] = id;
        return id;
    }

    /// <param name="email">The persona's email; <c>{name}@{org slug}.example</c> when omitted.</param>
    public async Task<World.Persona> CreatePersonaAsync(
        string name, Guid organizationId, Role orgRole, string? password = null, string? email = null)
    {
        Guid userId = await CreateUserAsync(organizationId, name, orgRole, password, email);
        var persona = new World.Persona(name, userId, organizationId);
        world.Personas[name] = persona;
        world.PersonaPasswords[name] = password ?? DefaultPassword;
        return persona;
    }

    public Task SuspendOrganizationAsync(Guid organizationId) => WithDbAsync(async db =>
    {
        Organization organization = await db.Organizations.SingleAsync(o => o.Id == organizationId);
        organization.Suspend();
        await db.SaveChangesAsync();
        return true;
    });

    /// <summary>
    /// Gives a seeded user a specific email. The domain has no "change email" operation, so this
    /// sets the mapped property directly — arrangement only, never a behaviour under test.
    /// </summary>
    public Task SetUserEmailAsync(Guid userId, string email) => WithDbAsync(async db =>
    {
        User user = await db.Users.SingleAsync(u => u.Id == userId);
        db.Entry(user).Property(u => u.Email).CurrentValue = email;
        await db.SaveChangesAsync();
        return true;
    });

    /// <summary>Deactivates a user through the domain, without going through the API.</summary>
    public Task DeactivateUserAsync(Guid userId) => WithDbAsync(async db =>
    {
        User user = await db.Users.SingleAsync(u => u.Id == userId);
        user.Deactivate();
        await db.SaveChangesAsync();
        return true;
    });

    /// <summary>The IDs of the given projects' boards and of the tasks on them — deleted ones included.</summary>
    public Task<List<Guid>> BoardAndTaskIdsOfProjectsAsync(IReadOnlyCollection<Guid> projectIds) => WithDbAsync(async db =>
    {
        List<Guid> boardIds = await db.Boards.IgnoreQueryFilters()
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        List<Guid> taskIds = await db.Tasks.IgnoreQueryFilters()
            .Where(t => boardIds.Contains(t.BoardId))
            .Select(t => t.Id)
            .ToListAsync();
        return boardIds.Concat(taskIds).ToList();
    });

    public Task<User> FindUserAsync(Guid userId) =>
        WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));

    public Task<Project?> FindProjectAsync(Guid projectId) =>
        WithDbAsync(db => db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId));

    public Task<Organization> FindOrganizationAsync(Guid organizationId) =>
        WithDbAsync(db => db.Organizations.AsNoTracking().SingleAsync(o => o.Id == organizationId));

    public Task<Guid> CreateProjectWithBoardsAsync(Guid organizationId, string projectName, params string[] boardNames) =>
        CreateProjectAsync(organizationId, projectName, description: null, boardNames);

    /// <summary>A project with a description (and optionally boards), founded by a hidden user.</summary>
    public async Task<Guid> CreateProjectAsync(Guid organizationId, string projectName, string? description, params string[] boardNames)
    {
        Guid founderId = await CreateUserAsync(organizationId, $"Founder of {projectName}", Role.Member);

        (Guid projectId, List<(string Name, Guid Id)> boards) = await WithDbAsync(async db =>
        {
            DateTimeOffset now = Now(db);
            Project project = Project.Create(organizationId, founderId, projectName, Slugify(projectName), now, description);
            db.Projects.Add(project);

            var created = new List<(string Name, Guid Id)>();
            foreach (string boardName in boardNames)
            {
                Board board = Board.Create(project.Id, boardName, founderId);
                db.Boards.Add(board);
                created.Add((boardName, board.Id));
            }

            await db.SaveChangesAsync();
            return (project.Id, created);
        });

        world.Projects[projectName] = projectId;
        world.ProjectFounders[projectId] = founderId;
        world.ProjectOrganizations[projectId] = organizationId;
        foreach ((string name, Guid boardId) in boards)
        {
            world.Boards[name] = boardId;
            world.BoardProjects[boardId] = projectId;
        }

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

    /// <summary>Adds boards to an existing project, in the given order, created by one of its members.</summary>
    public async Task CreateBoardsAsync(Guid projectId, params string[] boardNames)
    {
        Guid creatorId = await AnyProjectUserAsync(projectId);

        List<(string Name, Guid Id)> boards = await WithDbAsync(async db =>
        {
            var created = new List<(string Name, Guid Id)>();
            foreach (string boardName in boardNames)
            {
                Board board = Board.Create(projectId, boardName, creatorId);
                db.Boards.Add(board);
                created.Add((boardName, board.Id));
            }

            await db.SaveChangesAsync();
            return created;
        });

        foreach ((string name, Guid boardId) in boards)
        {
            world.Boards[name] = boardId;
            world.BoardProjects[boardId] = projectId;
        }
    }

    /// <summary>Reads a board as stored (boards of a deleted project are hidden by the query filter).</summary>
    public Task<Board?> FindBoardAsync(Guid boardId) =>
        WithDbAsync(db => db.Boards.AsNoTracking().SingleOrDefaultAsync(b => b.Id == boardId));

    /// <summary>A project's members as stored, in joining order.</summary>
    public Task<List<ProjectMember>> ProjectMembersAsync(Guid projectId) =>
        WithDbAsync(db => db.Projects
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Members)
            .AsNoTracking()
            .OrderBy(m => m.JoinedAt)
            .ThenBy(m => m.Id)
            .ToListAsync());

    /// <param name="dueInDays">Due date relative to now, or <see langword="null"/> for none.</param>
    /// <param name="reporterId">The task's reporter; defaults to the most privileged project member.</param>
    public async Task<Guid> CreateTaskAsync(
        Guid boardId, string title, Priority priority = Priority.Medium, int? dueInDays = null, string? description = null,
        Guid? reporterId = null)
    {
        Guid projectId = world.BoardProjects[boardId];
        Guid reporter = reporterId ?? await AnyProjectUserAsync(projectId);

        Guid taskId = await WithDbAsync(async db =>
        {
            DateTimeOffset now = Now(db);
            ProjectTask task = ProjectTask.Create(
                boardId, reporter, title, now, priority, description,
                dueDate: dueInDays is int days ? now.AddDays(days) : null);
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return task.Id;
        });

        world.Tasks[title] = taskId;
        world.TaskCreationOrder.Add(taskId);
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

    public Task RemoveProjectMemberAsync(Guid projectId, Guid userId) => WithDbAsync(async db =>
    {
        Project project = await db.Projects.Include(p => p.Members).SingleAsync(p => p.Id == projectId);
        project.RemoveMember(userId);
        await db.SaveChangesAsync();
        return true;
    });

    /// <summary>The hidden org-Admin observer of the organization a board belongs to.</summary>
    public Task<Guid> ObserverForBoardAsync(Guid boardId) =>
        ObserverForOrganizationAsync(world.ProjectOrganizations[world.BoardProjects[boardId]]);

    public Task ArchiveBoardAsync(Guid boardId) => WithDbAsync(async db =>
    {
        Board board = await db.Boards.SingleAsync(b => b.Id == boardId);
        board.Archive();
        await db.SaveChangesAsync();
        return true;
    });

    public Task ArchiveProjectAsync(Guid projectId) => WithDbAsync(async db =>
    {
        Project project = await db.Projects.SingleAsync(p => p.Id == projectId);
        project.Archive();
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

    /// <summary>The organization a task belongs to — even when the task or its project is deleted.</summary>
    public Task<Guid> OrganizationOfTaskAsync(Guid taskId) => WithDbAsync(db =>
        (from task in db.Tasks.IgnoreQueryFilters()
         join board in db.Boards.IgnoreQueryFilters() on task.BoardId equals board.Id
         join project in db.Projects.IgnoreQueryFilters() on board.ProjectId equals project.Id
         where task.Id == taskId
         select project.OrganizationId).SingleAsync());

    /// <summary>The current version of a task, formatted exactly like the API's ETag.</summary>
    /// <remarks>Includes soft-deleted tasks, so "delete it again" scenarios still send a real version.</remarks>
    public async Task<string> CurrentTaskVersionAsync(Guid taskId)
    {
        byte[]? rowVersion = await WithDbAsync(db =>
            db.Tasks.IgnoreQueryFilters().Where(t => t.Id == taskId).Select(t => t.RowVersion).SingleAsync());
        return $"\"{Convert.ToBase64String(rowVersion ?? [])}\"";
    }

    /// <summary>The current version of a project, formatted exactly like the API's ETag.</summary>
    /// <remarks>Includes soft-deleted projects, so "delete it again" scenarios still send a real version.</remarks>
    public async Task<string> CurrentProjectVersionAsync(Guid projectId)
    {
        byte[]? rowVersion = await WithDbAsync(db =>
            db.Projects.IgnoreQueryFilters().Where(p => p.Id == projectId).Select(p => p.RowVersion).SingleAsync());
        return $"\"{Convert.ToBase64String(rowVersion ?? [])}\"";
    }

    /// <summary>The current version of a board, formatted exactly like the API's ETag.</summary>
    public async Task<string> CurrentBoardVersionAsync(Guid boardId)
    {
        byte[]? rowVersion = await WithDbAsync(db =>
            db.Boards.Where(b => b.Id == boardId).Select(b => b.RowVersion).SingleAsync());
        return $"\"{Convert.ToBase64String(rowVersion ?? [])}\"";
    }

    /// <summary>The current version of a user, formatted exactly like the API's ETag.</summary>
    public async Task<string> CurrentUserVersionAsync(Guid userId)
    {
        byte[]? rowVersion = await WithDbAsync(db =>
            db.Users.Where(u => u.Id == userId).Select(u => u.RowVersion).SingleAsync());
        return $"\"{Convert.ToBase64String(rowVersion ?? [])}\"";
    }

    /// <summary>The current version of an organization, formatted exactly like the API's ETag.</summary>
    public async Task<string> CurrentOrganizationVersionAsync(Guid organizationId)
    {
        byte[]? rowVersion = await WithDbAsync(db =>
            db.Organizations.Where(o => o.Id == organizationId).Select(o => o.RowVersion).SingleAsync());
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

    private async Task<Guid> CreateUserAsync(
        Guid organizationId, string displayName, Role orgRole, string? password = null, string? email = null)
    {
        string orgSlug = await WithDbAsync(db =>
            db.Organizations.Where(o => o.Id == organizationId).Select(o => o.Slug).SingleAsync());

        // Only a non-default password pays for its own (deliberately slow) hash.
        string passwordHash = password is null || password == DefaultPassword
            ? DefaultPasswordHash.Value
            : HashPassword(password);

        return await WithDbAsync(async db =>
        {
            User user = User.Create(
                organizationId, displayName, email ?? $"{Slugify(displayName)}@{orgSlug}.example", passwordHash, orgRole);
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
