using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Security;

/// <summary>
/// Re-checks an already cryptographically valid access token against the user's <b>current</b>
/// state, once per authenticated request (called from the JWT bearer <c>OnTokenValidated</c>
/// event in <c>Ordinis.Api</c>).
/// </summary>
/// <remarks>
/// <para>
/// A JWT is self-contained: until it expires (15 minutes) it keeps proving whatever was true when
/// it was issued. Without this check a deactivated user kept full access, and a demoted org Admin
/// kept Admin authority (<c>IsOrgAdminOf</c> trusts the role claim), for the rest of the token's
/// lifetime.
/// </para>
/// <para>
/// The cost is one primary-key lookup per authenticated request. Deliberately uncached: a cache
/// would reintroduce a (shorter) window in which a deactivated user still gets in.
/// </para>
/// </remarks>
public interface ITokenPrincipalRevalidator
{
    /// <summary>
    /// Returns <see langword="false"/> if the token must be rejected (unknown or inactive user, or
    /// an organization that no longer matches). Otherwise replaces the principal's role claim with
    /// the user's current org role and returns <see langword="true"/>.
    /// </summary>
    Task<bool> RevalidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ITokenPrincipalRevalidator"/>
internal sealed class TokenPrincipalRevalidator(AppDbContext db) : ITokenPrincipalRevalidator
{
    public async Task<bool> RevalidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.Identity is not ClaimsIdentity identity
            || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out Guid userId))
        {
            return false;
        }

        var current = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.OrgRole, u.OrganizationId })
            .SingleOrDefaultAsync(cancellationToken);

        if (current is null || !current.IsActive)
        {
            return false;
        }

        // Users never change organization, so a mismatch means the token is not this user's.
        if (principal.FindFirst("org_id")?.Value != current.OrganizationId.ToString())
        {
            return false;
        }

        // Authorize with the role the user has *now*: a demotion takes effect immediately, and a
        // promotion doesn't wait for the next refresh. The next refreshed token carries it anyway.
        foreach (Claim staleRole in identity.FindAll(identity.RoleClaimType).ToList())
        {
            identity.RemoveClaim(staleRole);
        }

        identity.AddClaim(new Claim(identity.RoleClaimType, current.OrgRole.ToString()));
        return true;
    }
}
