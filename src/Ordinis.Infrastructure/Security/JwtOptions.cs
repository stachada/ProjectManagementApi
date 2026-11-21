namespace Ordinis.Infrastructure.Security;

/// <summary>
/// Bound from the <c>Jwt</c> configuration section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded signing key. Set via User Secrets (dev) / env var (prod) - never committed.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
