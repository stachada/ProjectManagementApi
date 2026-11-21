using Microsoft.AspNetCore.Authorization;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security.Authorization;

public enum ProjectResourceType { Project, Board, Task }

/// <summary>
/// Requires the caller to be a member (optionally at or above <paramref name="MinimumRole"/>)
/// of the project reached from the route value named <paramref name="RouteKey"/>, resolved
/// according to <paramref name="ResourceType"/>.
/// </summary>
public sealed class ProjectScopeRequirement(
    ProjectResourceType resourceType,
    string routeKey,
    Role? minimumRole = null) : IAuthorizationRequirement
{
    public ProjectResourceType ResourceType { get; } = resourceType;
    public string RouteKey { get; } = routeKey;
    public Role? MinimumRole { get; } = minimumRole;
}
