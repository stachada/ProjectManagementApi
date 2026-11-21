using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security.Authorization;

/// <summary>
/// Resolves the project behind the current request's route value and checks the caller's
/// access to it: an org-level <see cref="Role.Admin"/> of the project's own organization passes
/// every requirement; anyone else needs a <see cref="Domain.Projects.ProjectMember"/> role at or
/// above <see cref="ProjectScopeRequirement.MinimumRole"/>.
/// </summary>
/// <remarks>
/// The org Admin rule mirrors <c>CurrentUserServiceExtensions.GetAllowedProjectIds</c>, which
/// already lists every project in the organization for an org Admin - without it, an org Admin
/// would see a project in <c>GET /projects</c> and then get <c>404</c> opening it. The project
/// owner (its creator) needs no separate rule: <see cref="Project.Create"/> adds them as an
/// Admin member, and the last-Admin invariant keeps the project from ever losing its last Admin.
/// </remarks>
public sealed class ProjectScopeAuthorizationHandler(
    IAppDbContext db,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<ProjectScopeRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectScopeRequirement requirement)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;

        if (httpContext is null
            || !context.User.TryGetUserId(out Guid userId)
            || httpContext.GetRouteValue(requirement.RouteKey) is not string routeValue
            || !Guid.TryParse(routeValue, out Guid resourceId))
        {
            return;
        }

        string resourceType = requirement.ResourceType switch
        {
            ProjectResourceType.Project => nameof(Project),
            ProjectResourceType.Board => nameof(Board),
            ProjectResourceType.Task => nameof(ProjectTask),
            _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement.ResourceType, null)
        };

        IQueryable<Guid> projectIds = requirement.ResourceType switch
        {
            ProjectResourceType.Project => db.Projects
                .Where(p => p.Id == resourceId)
                .Select(p => p.Id),
            ProjectResourceType.Board => db.Boards
                .Where(b => b.Id == resourceId)
                .Select(b => b.ProjectId),
            ProjectResourceType.Task => db.Tasks
                .Where(t => t.Id == resourceId)
                .Join(db.Boards, t => t.BoardId, b => b.Id, (t, b) => b.ProjectId),
            _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement.ResourceType, null)
        };

        var project = await db.Projects
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.OrganizationId })
            .SingleOrDefaultAsync();

        if (project is not null && context.User.IsOrgAdminOf(project.OrganizationId))
        {
            context.Succeed(requirement);
            return;
        }

        // Role is stored as a string column, so it's loaded and compared in C# - a ">=" inside the
        // query would compare text ('Admin' < 'Member' < 'Viewer'), not enum order.
        Role? memberRole = project is null
            ? null
            : await db.ProjectMembers
                .Where(m => m.ProjectId == project.Id && m.UserId == userId)
                .Select(m => (Role?)m.Role)
                .SingleOrDefaultAsync();

        if (memberRole is null)
        {
            // Missing resource or not a member: indistinguishable to the caller (404).
            context.Fail(new ResourceNotVisibleReason(this, resourceType, resourceId));
            return;
        }

        if (requirement.MinimumRole is null || memberRole >= requirement.MinimumRole)
        {
            context.Succeed(requirement);
        }
    }
}
