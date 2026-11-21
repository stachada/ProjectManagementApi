using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security;

/// <summary>
/// Resolves <see cref="ICurrentUserService"/> from the current HTTP request's validated
/// access token claims. Registered scoped - one instance per request.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid UserId => Guid.Parse(RequireClaim(JwtRegisteredClaimNames.Sub));

    public Guid OrganizationId => Guid.Parse(RequireClaim("org_id"));

    public bool IsOrgAdmin => httpContextAccessor.HttpContext?.User.IsInRole(nameof(Role.Admin)) ?? false;

    private string RequireClaim(string claimType) =>
        httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value
        ?? throw new InvalidOperationException(
            $"No HttpContext or missing '{claimType}' claim - {nameof(CurrentUserService)} must only " +
            "be resolved within an authenticated HTTP request.");
}
