using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
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
    public IDbConnection GetDbConnection() => Database.GetDbConnection();

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
    ///   <item>Assigns a fresh <see cref="AggregateRoot.RowVersion"/> to all tracked
    ///         <see cref="AggregateRoot"/> instances — the app-managed concurrency token.</item>
    ///   <item>Serializes pending domain events from all tracked <see cref="AggregateRoot"/> instances
    ///         into <see cref="OutboxMessage"/> rows, then clears the in-memory event list so the
    ///         same events are never double-dispatched if the unit of work is reused.</item>
    /// </list>
    /// All three steps run before <c>base.SaveChangesAsync</c> so that the outbox rows and
    /// aggregate changes are committed in a single atomic transaction.
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        SetAuditTimestamps(now);
        SetConcurrencyTokens();
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

    /// <summary>
    /// Assigns a fresh <c>RowVersion</c> to every inserted/updated aggregate root. This is an
    /// app-managed concurrency token (see <see cref="AggregateRoot.RowVersion"/>) rather than a
    /// database-generated one, so the same behavior applies identically on SQL Server and
    /// PostgreSQL.
    /// </summary>
    private void SetConcurrencyTokens()
    {
        MarkAggregateRootsDirtyForChangedChildren();

        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RowVersion = Guid.CreateVersion7().ToByteArray();
        }
    }

    /// <summary>
    /// EF Core only marks an entity <c>Modified</c> when one of its own scalar/complex properties
    /// changes - adding, updating, or removing a child entity that lives in a separate table (e.g.
    /// <c>ProjectMember</c> under <see cref="Project"/>) leaves the owning aggregate root
    /// <c>Unchanged</c>, so it never participates in the <c>UPDATE</c> batch and its
    /// <c>RowVersion</c> is never reassigned or checked - a concurrent child mutation could never
    /// be detected as conflicting with it. Since the aggregate root is the DDD consistency
    /// boundary, any child mutation is conceptually an aggregate mutation: this walks every
    /// non-root tracked entry that changed and, if one of its navigation references points at a
    /// still-<c>Unchanged</c> aggregate root, marks that root <c>Modified</c> too so it gets a
    /// fresh <c>RowVersion</c> and a real optimistic-concurrency check on save.
    /// </summary>
    private void MarkAggregateRootsDirtyForChangedChildren()
    {
        foreach (EntityEntry entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AggregateRoot || entry.State == EntityState.Unchanged)
            {
                continue;
            }

            // Walk navigation metadata (not entry.References/entry.Navigations) and resolve the
            // owner via foreign-key property values rather than navigation fixup: a Deleted
            // entry's ReferenceEntry.TargetEntry is always null (EF Core severs the fixup once an
            // entity is marked for deletion), but its FK property values remain readable until the
            // DELETE command actually executes.
            foreach (INavigation navigation in entry.Metadata.GetNavigations())
            {
                if (navigation.IsCollection || navigation.Inverse is not { IsCollection: true })
                {
                    // Only follow the navigation whose inverse is a collection on the other side -
                    // that's the "owning aggregate root's collection" signal (e.g. ProjectMember.Project,
                    // whose inverse is Project.Members). A plain reference with no collection inverse
                    // (e.g. ProjectMember.User) is not an ownership relationship and must not cascade.
                    continue;
                }

                IForeignKey foreignKey = navigation.ForeignKey;
                var fkValues = foreignKey.Properties
                    .Select(p => entry.Property(p.Name).CurrentValue)
                    .ToArray();

                EntityEntry? owner = ChangeTracker.Entries()
                    .FirstOrDefault(e =>
                        e.Entity is AggregateRoot
                        && e.Metadata == foreignKey.PrincipalEntityType
                        && foreignKey.PrincipalKey.Properties
                            .Select(p => e.Property(p.Name).CurrentValue)
                            .SequenceEqual(fkValues));

                if (owner is { State: EntityState.Unchanged })
                {
                    owner.State = EntityState.Modified;
                }
            }
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
