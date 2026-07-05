using System.Security.Cryptography;
using System.Text;

namespace Ordinis.Application.Common;

/// <summary>
/// Development placeholder. Phase 8 replaces this with a BCrypt implementation in
/// <c>Ordinis.Infrastructure</c> once the auth package is installed.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    public string Hash(string plaintext)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
    public bool Verify(string plaintext, string hash)
        => Hash(plaintext).Equals(hash, StringComparison.Ordinal);
}
