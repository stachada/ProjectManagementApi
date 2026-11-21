using Ordinis.Application.Common;

namespace Ordinis.Infrastructure.Security;

/// <summary>
/// BCrypt-based implementation of <see cref="IPasswordHasher"/>
/// </summary>
/// <remarks>
/// Work factor 12 (~250ms/hash on typical 2026 hardware) balances brute-force resistance
/// against long/signup latency. BCrypt embeds the algorithm version, work factor, and a
/// random per-hash salt in the return string (e.g. <c>$2a$12$...</c>), so <see cref="Verify"/>
/// needs no separately stored salt.
/// </remarks>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return BCrypt.Net.BCrypt.EnhancedHashPassword(plaintext, WorkFactor);
    }

    public bool Verify(string plaintext, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        // EnhancedVerify guards against BCrypt's silent truncation of inputs over 72 bytes,
        // which the plain Verify overload is subject to.
        return BCrypt.Net.BCrypt.EnhancedVerify(plaintext, hash);
    }
}
