namespace Ordinis.Application.Common;

/// <summary>
/// Builds the caller-scoping subquery shared by every unscoped "list across everything"
/// query - <see cref="Tasks.Queries.GetTasksFiltered"/> and
/// <see cref="Projects.Queries.GetProjectsFiltered"/> today, any future one tomorrow.
/// </summary>
internal static class CurrentUserServiceExtensions
{
    /// <summary>
    /// The set of project IDs <paramref name="currentUser"/> is allowed to see: every project
    /// in their organization if they're an org Admin, otherwise only projects they're an
    /// explicit <see cref="Domain.Projects.ProjectMember"/> of.
    /// </summary>
    public static IQueryable<Guid> GetAllowedProjectIds(this ICurrentUserService currentUser, IAppDbContext db)
        => currentUser.IsOrgAdmin
            ? db.Projects.Where(p => p.OrganizationId == currentUser.OrganizationId).Select(p => p.Id)
            : db.ProjectMembers.Where(m => m.UserId == currentUser.UserId).Select(m => m.ProjectId);
}
