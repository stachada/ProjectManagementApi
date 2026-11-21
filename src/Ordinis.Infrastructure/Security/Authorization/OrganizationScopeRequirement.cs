using Microsoft.AspNetCore.Authorization;

namespace Ordinis.Infrastructure.Security.Authorization;

/// <summary>
/// What the route value of an <see cref="OrganizationScopeRequirement"/> identifies.
/// </summary>
public enum OrganizationResourceType
{
    /// <summary>The route value is an organization ID.</summary>
    Organization,

    /// <summary>The route value is a user ID; the user's own organization is checked.</summary>
    User
}

/// <summary>
/// Requires the resource reached from the route value named <see cref="RouteKey"/> to belong to
/// the caller's own organization (the access token's <c>org_id</c> claim), and optionally
/// requires the caller to hold the org-level Admin role.
/// </summary>
/// <remarks>
/// A resource in another organization fails as "not visible" (<c>404</c>, identical to a missing
/// resource) regardless of the caller's role, so tenants cannot probe each other. Only once the
/// resource is known to be in the caller's organization does a missing Admin role produce a
/// plain <c>403</c>.
/// </remarks>
/// <param name="resourceType">What the route value identifies.</param>
/// <param name="routeKey">The name of the route value holding the resource ID.</param>
/// <param name="requireOrgAdmin">Whether the caller must hold the org-level Admin role.</param>
/// <param name="allowSelf">
/// For <see cref="OrganizationResourceType.User"/> only: the target user themselves passes even
/// when <paramref name="requireOrgAdmin"/> is <see langword="true"/> (e.g. editing your own profile).
/// </param>
public sealed class OrganizationScopeRequirement(
    OrganizationResourceType resourceType,
    string routeKey,
    bool requireOrgAdmin = false,
    bool allowSelf = false) : IAuthorizationRequirement
{
    /// <summary>What the route value identifies.</summary>
    public OrganizationResourceType ResourceType { get; } = resourceType;

    /// <summary>The name of the route value holding the resource ID.</summary>
    public string RouteKey { get; } = routeKey;

    /// <summary>Whether the caller must hold the org-level Admin role.</summary>
    public bool RequireOrgAdmin { get; } = requireOrgAdmin;

    /// <summary>Whether the target user themselves passes the Admin requirement.</summary>
    public bool AllowSelf { get; } = allowSelf;
}
