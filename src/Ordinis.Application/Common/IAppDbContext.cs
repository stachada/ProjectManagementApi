using Ordinis.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Common;

/// <summary>
/// Defines the database access contract used by Application-layer command and query handlers.
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in <c>Ordinis.Application</c> - not in Infrstructure - so that
/// the dependency arrow points inward: Application defines what it needs; Infrastructure
/// provides the implementation (<c>AppDbContext</c>). This is the Dependency Inversion
/// Principle applied at the layer boundary.
/// </para>
/// <para>
/// Only the <see cref="DbSet{T}"/> properties and <see cref="SaveChangesAsync"/> that
/// handlers actually need are exposed here. Keeping the surface minimal means adding
/// a new aggregate root requires a deliberate, visible change to this contract rather
/// then silently becoming available everywhere.
/// </para>
/// <para>
/// EF Core's <see cref="DbSet{T}"/> is used directly rather than wrapping sets behind
/// a repository abstraction - consistent with the project-wide decision to inject
/// <c>AppDbContext</c> (via this interface) into handlers without an intermediate layer.
/// </para>
/// </remarks>
public interface IAppDbContext
{
    /// <summary>
    /// Tasks (ProjectTask aggregate root).
    /// </summary>
    DbSet<ProjectTask> Tasks { get; }

    /// <summary>
    /// Board - queried be validators to verify board existence before task creation.
    /// </summary>
    DbSet<Board> Boards { get; }

    /// <summary>
    /// Comments - queried directly by <c>EditCommentValidator</c> for author ownership checks.
    /// </summary>
    DbSet<Comment> Comments { get; }

    /// <summary>
    /// Users - queried by validators and query handlers for existence checks and display name resolution.
    /// </summary>
    DbSet<User> Users { get; }

    /// <summary>
    /// Projects
    /// </summary>
    DbSet<Project> Projects { get; }

    /// <summary>
    /// Organizations
    /// </summary>
    DbSet<Organization> Organizations { get; }

    /// <summary>
    /// Project members - queried by validators to verify membership before task creation and other operations.
    /// </summary>
    DbSet<ProjectMember> ProjectMembers { get; }

    /// <summary>
    /// Persists all pending changes to the database within the current transaction.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
