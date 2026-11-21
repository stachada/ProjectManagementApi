using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security.Authorization;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extracts the user ID from the <c>sub</c> claim <see cref="JwtTokenService"/> issues.
    /// </summary>
    public static bool TryGetUserId(
        this ClaimsPrincipal principal,
        out Guid userId)
        => Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

    /// <summary>
    /// Extracts the caller's organization ID from the <c>org_id</c> claim <see cref="JwtTokenService"/> issues.
    /// </summary>
    public static bool TryGetOrganizationId(
        this ClaimsPrincipal principal,
        out Guid organizationId)
        => Guid.TryParse(principal.FindFirst("org_id")?.Value, out organizationId);

    /// <summary>
    /// Whether the caller holds the org-level <see cref="Role.Admin"/> role for
    /// <paramref name="organizationId"/>. An org Admin's authority never crosses into another
    /// organization, so the organization must match the caller's own <c>org_id</c> claim.
    /// </summary>
    public static bool IsOrgAdminOf(
        this ClaimsPrincipal principal,
        Guid organizationId)
        => principal.IsInRole(nameof(Role.Admin))
            && principal.TryGetOrganizationId(out Guid callerOrganizationId)
            && callerOrganizationId == organizationId;
}
