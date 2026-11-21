namespace Ordinis.Application.Common;

/// <summary>
/// Ambient identity of the caller making the current request. Resolved once per request from
/// the access token's claims - Application handlers depend on this abstraction only, never
/// <c>ClaimsPrincipal</c>/<c>HttpContext</c> directly, same boundary <see cref="IAppDbContext"/>
/// already enforces for presistance.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The caller's user ID (the acess token's <c>sub</c> claim).
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// The caller's organization ID (the access token's <c>org_id</c> claim).
    /// </summary>
    Guid OrganizationId { get; }

    /// <summary>
    /// Whether the caller holds the org-level <see cref="Domain.Users.Role.Admin"/> role.
    /// </summary>
    bool IsOrgAdmin { get; }
}
