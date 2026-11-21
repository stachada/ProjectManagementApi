using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.Benchmarks.Infrastructure;

/// <summary>
/// Identifiers and credentials of the seeded benchmark dataset.
/// </summary>
/// <param name="OrganizationId">The benchmark organization.</param>
/// <param name="AdminUserId">The org Admin every benchmark and the load test act as.</param>
/// <param name="ProjectId">The single benchmark project.</param>
/// <param name="TaskCount">Number of live tasks in the dataset.</param>
internal sealed record SeededDataset(Guid OrganizationId, Guid AdminUserId, Guid ProjectId, int TaskCount)
{
    /// <summary>Organization slug for <c>POST /auth/login</c>.</summary>
    public const string OrganizationSlug = "ordinis-benchmarks";

    /// <summary>Admin email for <c>POST /auth/login</c>.</summary>
    public const string AdminEmail = "admin@benchmarks.ordinis.local";

    /// <summary>
    /// Admin password for <c>POST /auth/login</c>. A fixed, published value is fine: this
    /// account only ever exists in throwaway benchmark/load-test databases.
    /// </summary>
    public const string AdminPassword = "Benchmark!Passw0rd";
}

/// <summary>
/// Seeds a deterministic, realistically shaped dataset through the domain model and
/// <see cref="IAppDbContext"/> - the same path production writes take - so every column,
/// default and outbox row looks exactly as the API would have produced it.
/// </summary>
/// <remarks>
/// <para>
/// Shape: 1 organization, 50 users, 1 project (all users members), 5 boards, 10&#160;000 tasks
/// spread uniformly across boards, title topics, priorities and statuses; ~80&#160;% assigned;
/// 0-3 comments (~15&#160;000 total) and a 1-in-3 chance of one attachment per task.
/// </para>
/// <para>
/// Every attribute is drawn independently from a <see cref="Random"/> with a fixed seed, so runs
/// are reproducible but columns are uncorrelated. (Deriving them all from <c>i % n</c> looks
/// deterministic too, but silently correlates them - e.g. every "Billing" task landing on the
/// same priority - which makes combined filters return nothing.)
/// </para>
/// </remarks>
internal static class BenchmarkDataSeeder
{
    /// <summary>Number of tasks the dataset contains.</summary>
    public const int TaskCount = 10_000;

    private const int UserCount = 50;
    private const int BoardCount = 5;
    private const int BatchSize = 1_000;
    private const int RandomSeed = 20_261_121;

    // Title keywords - "billing" is the search term the filtered query benchmark uses
    // (~1 in 8 titles, so a search scans everything but matches a realistic slice).
    private static readonly string[] TitleTopics =
        ["Login", "Billing", "Dashboard", "Export", "Search", "Onboarding", "Payments", "Reporting"];

    private static readonly ProjectTaskStatus[] ForwardPath =
        [ProjectTaskStatus.ToDo, ProjectTaskStatus.InProgress, ProjectTaskStatus.InReview, ProjectTaskStatus.Done];

    /// <summary>
    /// Seeds the dataset unless the benchmark organization already exists, and returns its
    /// identifiers either way.
    /// </summary>
    public static async Task<SeededDataset> SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        SeededDataset? existing = await FindExistingAsync(services, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        Console.WriteLine($"// Seeding benchmark dataset ({TaskCount:N0} tasks) - first run only...");

        TimeProvider timeProvider = services.GetRequiredService<TimeProvider>();
        DateTimeOffset now = timeProvider.GetUtcNow();

        (Organization organization, List<User> users, Project project, List<Board> boards) =
            await SeedOrganizationAsync(services, now, cancellationToken);

        Guid adminId = users[0].Id;
        var random = new Random(RandomSeed);

        for (int batchStart = 0; batchStart < TaskCount; batchStart += BatchSize)
        {
            // A fresh scope per batch keeps the change tracker small - one 10 000-task
            // SaveChanges would spend most of its time in DetectChanges.
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            int batchEnd = Math.Min(batchStart + BatchSize, TaskCount);
            for (int i = batchStart; i < batchEnd; i++)
            {
                db.Tasks.Add(CreateTask(i, random, boards, users, adminId, now));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        Console.WriteLine("// Seeding complete.");

        return new SeededDataset(organization.Id, adminId, project.Id, TaskCount);
    }

    private static async Task<SeededDataset?> FindExistingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        Organization? organization = await db.Organizations
            .SingleOrDefaultAsync(o => o.Slug == SeededDataset.OrganizationSlug, cancellationToken);

        if (organization is null)
        {
            return null;
        }

        Guid adminId = await db.Users
            .Where(u => u.OrganizationId == organization.Id && u.Email == SeededDataset.AdminEmail)
            .Select(u => u.Id)
            .SingleAsync(cancellationToken);

        Guid projectId = await db.Projects
            .Where(p => p.OrganizationId == organization.Id)
            .Select(p => p.Id)
            .SingleAsync(cancellationToken);

        int taskCount = await db.Tasks.CountAsync(
            t => db.Boards.Any(b => b.Id == t.BoardId && b.ProjectId == projectId),
            cancellationToken);

        if (taskCount != TaskCount)
        {
            throw new InvalidOperationException(
                $"Benchmark database is partially seeded ({taskCount:N0}/{TaskCount:N0} tasks) - " +
                $"an earlier seeding run was interrupted. Drop the OrdinisBenchmarks database " +
                $"(or the ordinis-benchmarks-mssql container) and run again.");
        }

        return new SeededDataset(organization.Id, adminId, projectId, taskCount);
    }

    private static async Task<(Organization, List<User>, Project, List<Board>)> SeedOrganizationAsync(
        IServiceProvider services,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        IPasswordHasher passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var organization = Organization.Create(
            "Ordinis Benchmarks",
            SeededDataset.OrganizationSlug,
            "Synthetic dataset for BenchmarkDotNet and k6 load tests.");

        // Every user shares one hash - hashing is deliberately slow, and only the admin logs in.
        string passwordHash = passwordHasher.Hash(SeededDataset.AdminPassword);

        var users = new List<User>(UserCount)
        {
            User.Create(organization.Id, "Benchmark Admin", SeededDataset.AdminEmail, passwordHash, Role.Admin),
        };

        for (int u = 1; u < UserCount; u++)
        {
            users.Add(User.Create(
                organization.Id,
                $"Benchmark User {u:D2}",
                $"user{u:D2}@benchmarks.ordinis.local",
                passwordHash,
                Role.Member));
        }

        // Project.Create adds the creator (users[0]) as a project Admin automatically.
        var project = Project.Create(
            organization.Id,
            users[0].Id,
            "Benchmark Project",
            "benchmark-project",
            now,
            "Holds every benchmark task.");

        foreach (User member in users.Skip(1))
        {
            project.AddMember(member.Id, Role.Member, now);
        }

        var boards = Enumerable.Range(1, BoardCount)
            .Select(n => Board.Create(project.Id, $"Board {n}", users[0].Id))
            .ToList();

        db.Organizations.Add(organization);
        db.Users.AddRange(users);
        db.Projects.Add(project);
        db.Boards.AddRange(boards);
        await db.SaveChangesAsync(cancellationToken);

        return (organization, users, project, boards);
    }

    private static ProjectTask CreateTask(
        int i,
        Random random,
        List<Board> boards,
        List<User> users,
        Guid adminId,
        DateTimeOffset now)
    {
        string topic = TitleTopics[random.Next(TitleTopics.Length)];
        bool hasDescription = random.Next(3) != 0;
        bool hasDueDate = random.Next(7) != 0;

        var task = ProjectTask.Create(
            boardId: boards[random.Next(boards.Count)].Id,
            reporterId: users[random.Next(users.Count)].Id,
            title: $"{topic} task #{i:D5}",
            now: now,
            priority: (Priority)random.Next(4),
            description: hasDescription ? $"Synthetic {topic.ToLowerInvariant()} work item number {i}." : null,
            // ProjectTask.Create only accepts future due dates; spread them over the next 90 days.
            dueDate: hasDueDate ? now.AddDays(1 + random.Next(90)) : null);

        if (random.Next(5) != 0) // ~80 % assigned
        {
            task.Assign(users[random.Next(users.Count)].Id, adminId, now);
        }

        // Walk 0-4 steps along the legal forward path (ToDo -> InProgress -> InReview -> Done),
        // going through Move() so every status is one the domain could actually reach.
        int steps = random.Next(ForwardPath.Length + 1);
        foreach (ProjectTaskStatus target in ForwardPath.Take(steps))
        {
            if (task.Status != target && task.Status.GetAllowedTransitions().Contains(target))
            {
                task.Move(target, adminId, now);
            }
        }

        int commentCount = random.Next(4);
        for (int c = 0; c < commentCount; c++)
        {
            task.AddComment($"Comment {c + 1} on task {i}.", users[random.Next(users.Count)].Id, now);
        }

        if (random.Next(3) == 0)
        {
            task.AddAttachment(
                fileName: $"spec-{i:D5}.pdf",
                contentType: "application/pdf",
                sizeInBytes: 1_024L * (random.Next(500) + 1),
                storageUrl: $"/attachments/benchmarks/spec-{i:D5}.pdf",
                uploadedByUserId: adminId,
                now: now);
        }

        return task;
    }
}
