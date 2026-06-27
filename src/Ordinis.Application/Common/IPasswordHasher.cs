namespace Ordinis.Application.Common;

/// <summary>
/// Abstracts password hashing so the Application layer never depends on a
/// specific algorithm (BCrypt, Argon2, etc.).
/// </summary>
/// <remarks>
/// <para>
/// The single implementation lives in <c>Ordinis.Infrastructure</c> and is
/// registered by <c>AddInfrastructureServices()</c>. Swapping algorithms is
/// a one-class change with no impact on any handler.
/// </para>
/// <para>
/// Hashing always happens in the Application layer - command handlers call
/// <see cref="Hash"/> before passing the result into domain methods.
/// The domain only ever stores and compares hashes; it never sees plaintext.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Produces a one-way hash of <paramref name="plaintext"/> suitable for
    /// persistent storage.
    /// </summary>
    /// <param name="plaintext">The raw password supplied by the user.</param>
    /// <returns>
    /// An algorithm-prefixed hash string (e.g. a BCrypt <c>$2a$...</c> value).
    /// </returns>
    string Hash(string plaintext);

    /// <summary>
    /// Verifies that <paramref name="plaintext"/> matches a previously
    /// produced <paramref name="hash"/>.
    /// </summary>
    /// <param name="plaintext">The raw password to verify.</param>
    /// <param name="hash">The stored hash to compare against.</param>
    /// <returns>
    /// <c>true</c> if the plaintext matches the hash; otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Included here rather than on a separate interface because both
    /// operations share the same algorithm context and are always implemented
    /// together. Auth handlers will use <see cref="Verify"/>
    /// to validate login credentials.
    /// </remarks>
    bool Verify(string plaintext, string hash);
}
