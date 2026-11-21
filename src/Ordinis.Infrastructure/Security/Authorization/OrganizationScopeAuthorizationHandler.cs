using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security.Authorization;

/// <summary>
/// Evaluates <see cref="OrganizationScopeRequirement"/>: resolves the organization behind the
/// route value and checks it against the caller's <c>org_id</c> claim, then (optionally) the
/// caller's org-level Admin role.
/// </summary>
public sealed class OrganizationScopeAuthorizationHandler(
    IAppDbContext db,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<OrganizationScopeRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationScopeRequirement requirement)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;

        if (httpContext is null
            || !context.User.TryGetUserId(out Guid userId)
            || !context.User.TryGetOrganizationId(out Guid callerOrganizationId)
            || httpContext.GetRouteValue(requirement.RouteKey) is not string routeValue
            || !Guid.TryParse(routeValue, out Guid resourceId))
        {
            return;
        }

        (string resourceType, Guid? resourceOrganizationId) = requirement.ResourceType switch
        {
            OrganizationResourceType.Organization => (
                nameof(Organization),
                await db.Organizations.AnyAsync(o => o.Id == resourceId) ? resourceId : (Guid?)null),
            OrganizationResourceType.User => (
                nameof(User),
                await db.Users
                    .Where(u => u.Id == resourceId)
                    .Select(u => (Guid?)u.OrganizationId)
                    .SingleOrDefaultAsync()),
            _ => throw new ArgumentOutOfRangeException(nameof(requirement), requirement.ResourceType, null)
        };

        if (resourceOrganizationId != callerOrganizationId)
        {
            // Missing resource or another tenant's resource: indistinguishable to the caller (404).
            context.Fail(new ResourceNotVisibleReason(this, resourceType, resourceId));
            return;
        }

        bool isSelf = requirement.AllowSelf
            && requirement.ResourceType == OrganizationResourceType.User
            && resourceId == userId;

        if (!requirement.RequireOrgAdmin || isSelf || context.User.IsInRole(nameof(Role.Admin)))
        {
            context.Succeed(requirement);
        }
    }
}
