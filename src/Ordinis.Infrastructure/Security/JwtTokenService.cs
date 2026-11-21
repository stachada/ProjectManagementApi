using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ordinis.Application.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Infrastructure.Security;

/// <summary>
/// Implementation of <see cref="IJwtTokenService"/> that issues signed JWT access tokens and hashes refresh tokens.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>
    /// Generates a signed JWT access token for the given user, containing claims for the user's identity and role.
    /// The token is signed using the configured signing key and is valid for the configured access token
    /// </summary>
    /// <param name="user">The user for whom to generate the access token.</param>
    /// <param name="now">The current date and time.</param>
    /// <returns>The generated JWT access token as a string.</returns>
    public (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user, DateTimeOffset now)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        DateTimeOffset expiresAt = now.AddMinutes(_options.AccessTokenExpiryMinutes);

        Claim[] claims =
        [
            new (JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new (JwtRegisteredClaimNames.Email, user.Email),
            new ("name", user.DisplayName),
            new ("org_id", user.OrganizationId.ToString()),
            new (ClaimTypes.Role, user.OrgRole.ToString()),
            new (JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        ];

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// Deterministically hashes a refresh token for storage/lookup. Not BCrypt - refresh
    /// tokens must be found by equality against a presented value, which a salted hash
    /// can't support.
    /// </summary>
    public (string RefreshToken, DateTimeOffset ExpiresAt) GenerateRefreshToken(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return (Convert.ToBase64String(bytes), now.AddDays(_options.RefreshTokenExpiryDays));
    }

    /// <summary>
    /// Deterministically hashes a refresh token for storage/lookup. Not BCrypt - refresh
    /// tokens must be found by equality against a presented value, which a salted hash
    /// can't support.
    /// </summary>
    /// <param name="refreshToken">The refresh token to hash.</param>
    /// <returns>The hashed refresh token as a hexadecimal string.</returns>
    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
