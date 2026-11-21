using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security.Authorization;

/// <summary>
/// Evaluates <see cref="TaskOwnerRequirement"/>: the caller passes if they are the task's
/// assignee, an org-level <see cref="Role.Admin"/> of the task's organization, or at least a
/// <see cref="Role.Member"/> of the task's project. A caller who is none of those and not even a
/// project <see cref="Role.Viewer"/> gets <c>404</c>; a Viewer gets <c>403</c>.
/// </summary>
public sealed class TaskOwnerAuthorizationHandler(
    IAppDbContext db,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<TaskOwnerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TaskOwnerRequirement requirement)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;

        if (httpContext is null
            || !context.User.TryGetUserId(out Guid userId)
            || httpContext.GetRouteValue("id") is not string routeValue
            || !Guid.TryParse(routeValue, out Guid taskId))
        {
            return;
        }

        var task = await db.Tasks
            .Where(t => t.Id == taskId)
            .Join(db.Boards, t => t.BoardId, b => b.Id, (t, b) => new { t.AssigneeId, b.ProjectId })
            .Join(db.Projects, x => x.ProjectId, p => p.Id, (x, p) => new { x.AssigneeId, x.ProjectId, p.OrganizationId })
            .SingleOrDefaultAsync();

        if (task is null)
        {
            context.Fail(new ResourceNotVisibleReason(this, nameof(ProjectTask), taskId));
            return;
        }

        if (task.AssigneeId == userId || context.User.IsOrgAdminOf(task.OrganizationId))
        {
            context.Succeed(requirement);
            return;
        }

        // Loaded and compared in C# - see ProjectScopeAuthorizationHandler for why.
        Role? memberRole = await db.ProjectMembers
            .Where(m => m.ProjectId == task.ProjectId && m.UserId == userId)
            .Select(m => (Role?)m.Role)
            .SingleOrDefaultAsync();

        if (memberRole is null)
        {
            context.Fail(new ResourceNotVisibleReason(this, nameof(ProjectTask), taskId));
            return;
        }

        if (memberRole >= Role.Member)
        {
            context.Succeed(requirement);
        }
    }
}
