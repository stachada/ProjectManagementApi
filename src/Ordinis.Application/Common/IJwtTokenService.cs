using Ordinis.Domain.Users;

namespace Ordinis.Application.Common;

/// <summary>
/// Issues and hashes authentication tokens. The Application layer never touches a JWT
/// library directly - command handlers depend on this abstraction only.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issues a signed JWT access token carrying the user's identity and role claims.
    /// </summary>
    /// <param name="user">The user for whom the access token is being generated.</param>
    /// <param name="now">The current date and time.</param>
    /// <returns
    /// The generated JWT and its UTC expiry. Expiry is returned here, not recomputed by the
    /// caller, so <c>Ordinis.Application</c> never needs the Infrastructure-owned
    /// <c>JwtOptions.AccessTokenExpiryMinutes</c> this value is derived from.
    /// </returns>
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user, DateTimeOffset now);

    /// <summary>
    /// Generates a new high-entropy opaque refresh token (not a JWT).
    /// </summary>
    /// <param name=""now">The current date and time.</param>
    /// <returns>
    /// The generated refresh token and its UTC expiry -same rationale as
    /// <see cref="GenerateAccessToken"/> returning its own expiry.
    /// </returns>
    (string RefreshToken, DateTimeOffset ExpiresAt) GenerateRefreshToken(DateTimeOffset now);

    /// <summary>
    /// Deterministically hashes a refresh token for storage/lookup. Not BCrypt - refresh
    /// tokens must be found by equality against a presented value, which a salted hash
    /// can't support.
    /// </summary>
    /// <param name="refreshToken">The refresh token to hash.</param>
    /// <returns>The hashed refresh token as a string.</returns>
    string HashRefreshToken(string refreshToken);
}
