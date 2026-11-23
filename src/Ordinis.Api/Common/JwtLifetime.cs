using Microsoft.IdentityModel.Tokens;

namespace Ordinis.Api.Common;

/// <summary>
/// Checks an access token's lifetime (<c>nbf</c> / <c>exp</c>) against the app's
/// <see cref="TimeProvider"/>. IdentityModel's built-in check reads the system clock directly
/// (its own time-provider setting is internal), so this replaces it, with the same rules.
/// </summary>
public static class JwtLifetime
{
    /// <summary>A <see cref="LifetimeValidator"/> that takes "now" from <paramref name="timeProvider"/>.</summary>
    public static LifetimeValidator Validator(TimeProvider timeProvider) =>
        (notBefore, expires, _, parameters) =>
            IsValid(notBefore, expires, timeProvider.GetUtcNow().UtcDateTime, parameters.ClockSkew);

    /// <summary>
    /// Whether a token is valid at <paramref name="now"/>: it must state an expiry, must not be
    /// used before its <c>nbf</c>, and stays valid up to and including its <c>exp</c> — each
    /// boundary widened by <paramref name="clockSkew"/>, as in IdentityModel's own check.
    /// </summary>
    public static bool IsValid(DateTime? notBefore, DateTime? expires, DateTime now, TimeSpan clockSkew)
    {
        if (expires is not DateTime expiresAt)
        {
            return false;
        }

        if (notBefore is DateTime notBeforeAt && (notBeforeAt > expiresAt || notBeforeAt > now + clockSkew))
        {
            return false;
        }

        return expiresAt >= now - clockSkew;
    }
}
