using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Persistence;

/// <summary>
/// The application's single EF Core DbContext.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAppDbContext"/> so Application-layer handlers depend only on the
/// interface — they never reference this concrete class directly.
/// </para>
/// <para>
/// <b>Audit timestamps:</b> <see cref="SaveChangesAsync"/> sets <c>CreatedAt</c> on new
/// entities and <c>UpdatedAt</c> on all modified entities via the injected
/// <see cref="TimeProvider"/>. Domain entities never assign these themselves.
/// </para>
/// <para>
/// <b>Outbox:</b> Before delegating to <c>base.SaveChangesAsync</c>, pending domain events
/// are serialized to <see cref="OutboxMessage"/> rows and added to the change tracker.
/// They are committed atomically with the aggregate changes in the same database transaction.
/// </para>
/// </remarks>
public sealed class AppDbContext : DbContext, IAppDbContext
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes the context with EF Core options and a time source.
    /// </summary>
    /// <param name="options">Provider-specific EF Core options (SQL Server or PostgreSQL).</param>
    /// <param name="timeProvider">
    /// Injected time source. Use <c>TimeProvider.System</c> in production;
    /// <c>FakeTimeProvider</c> in tests.
    /// </param>
    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();

    /// <inheritdoc/>
    public DbSet<Board> Boards => Set<Board>();

    /// <inheritdoc/>
    public DbSet<Comment> Comments => Set<Comment>();

    /// <inheritdoc/>
    public DbSet<User> Users => Set<User>();

    /// <inheritdoc/>
    public DbSet<Project> Projects => Set<Project>();

    /// <inheritdoc/>
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <inheritdoc/>
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    /// <summary>
    /// Outbox messages — written by this context; read by <c>OutboxDispatcherJob</c>.
    /// Not exposed on <see cref="IAppDbContext"/> because Application handlers never write
    /// directly to the Outbox; that responsibility belongs here.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// In addition to persisting changes, this override:
    /// <list type="number">
    ///   <item>Sets <c>CreatedAt</c> / <c>UpdatedAt</c> on all tracked <see cref="AuditableEntity"/> instances.</item>
    ///   <item>Serializes pending domain events from all tracked <see cref="AggregateRoot"/> instances
    ///         into <see cref="OutboxMessage"/> rows, then clears the in-memory event list so the
    ///         same events are never double-dispatched if the unit of work is reused.</item>
    /// </list>
    /// Both operations run before <c>base.SaveChangesAsync</c> so that the outbox rows and
    /// aggregate changes are committed in a single atomic transaction.
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        SetAuditTimestamps(now);
        WriteOutboxMessages();

        return await base.SaveChangesAsync(cancellationToken);
    }

    private void SetAuditTimestamps(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;

            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
    }

    private void WriteOutboxMessages()
    {
        var aggregatesWithEvents = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregatesWithEvents.Count == 0)
        {
            return;
        }

        var outboxMessages = aggregatesWithEvents
            .SelectMany(a => a.DomainEvents)
            .Select(OutboxMessage.From)
            .ToList();

        OutboxMessages.AddRange(outboxMessages);

        foreach (AggregateRoot? aggregate in aggregatesWithEvents)
        {
            aggregate.ClearDomainEvents();
        }
    }
}
