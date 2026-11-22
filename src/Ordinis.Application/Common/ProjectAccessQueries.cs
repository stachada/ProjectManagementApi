using Microsoft.EntityFrameworkCore;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Common;

/// <summary>
/// Shared definition of "who may contribute to a project", for validators that need to check a
/// user other than the caller (e.g. a task's assignee) — authorization policies only ever check
/// the caller.
/// </summary>
/// <remarks>
/// Mirrors <c>ProjectScopeAuthorizationHandler</c>'s contributor rule: an org Admin of the
/// project's own organization, or a project member with role <see cref="Role.Member"/> or higher.
/// </remarks>
public static class ProjectAccessQueries
{
    /// <summary>
    /// Whether <paramref name="userId"/> may contribute to (create, change and be assigned tasks
    /// in) the project. Returns <see langword="false"/> for unknown projects or users, and for
    /// users of another organization.
    /// </summary>
    public static async Task<bool> CanContributeAsync(IAppDbContext db, Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        Guid? organizationId = await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken);

        if (organizationId is null)
        {
            return false;
        }

        bool isOrgAdmin = await db.Users.AnyAsync(
            u => u.Id == userId && u.OrgRole == Role.Admin && u.OrganizationId == organizationId,
            cancellationToken);

        if (isOrgAdmin)
        {
            return true;
        }

        // Load the role, then compare in C#. Role is stored as a string, so ">=" inside the query
        // would compare text ('Admin' < 'Member' < 'Viewer'), not enum order.
        Role? role = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .Select(m => (Role?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return role >= Role.Member; // null >= Member is false, so non-members fail too
    }

    /// <summary>The project a board belongs to, or <see langword="null"/> if the board does not exist.</summary>
    public static Task<Guid?> ProjectOfBoardAsync(IAppDbContext db, Guid boardId, CancellationToken cancellationToken) =>
        db.Boards.Where(b => b.Id == boardId).Select(b => (Guid?)b.ProjectId).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The project a task belongs to (via its board), or <see langword="null"/> if the task does not exist.</summary>
    public static Task<Guid?> ProjectOfTaskAsync(IAppDbContext db, Guid taskId, CancellationToken cancellationToken) =>
        db.Tasks
            .Where(t => t.Id == taskId)
            .Join(db.Boards, t => t.BoardId, b => b.Id, (t, b) => (Guid?)b.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
}
