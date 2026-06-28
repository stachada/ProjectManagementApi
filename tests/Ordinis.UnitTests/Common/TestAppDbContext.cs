using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.UnitTests.Common;

/// <summary>
/// Minimal EF Core InMemory-backed <see cref="IAppDbContext"/> double for Application-layer
/// handler unit tests. Not the real <c>AppDbContext</c> (Phase 5) - has no entity configuration,
/// no Outbox, no concurrency handling. Exists only so handler tests can exercise real EF Core
/// query/save behaviour without a database.
/// </summary>
internal sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    /// <summary>
    /// Every aggregate/entity in this domain generates its <c>Id</c> client-side
    /// (<c>Guid.CreateVersion7()</c>) rather than relying on the database. Without this,
    /// EF Core's default convention marks Guid keys <c>ValueGenerated.OnAdd</c>, and an entity
    /// discovered via cascading navigation fix-up (e.g. a new <c>Attachment</c> added to
    /// <c>ProjectTask.Attachments</c>) gets misidentified as already existing in the store,
    /// producing an UPDATE instead of an INSERT on save. The real <c>AppDbContext</c> (Phase 5)
    /// will set this per-entity in its <c>IEntityTypeConfiguration{T}</c> classes; here it's
    /// applied generically since there are no entity configurations yet.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.FindProperty("Id") is not null)
            {
                modelBuilder.Entity(entityType.ClrType).Property("Id").ValueGeneratedNever();
            }
        }
    }
}
