namespace Ordinis.Application.Common;

/// <summary>
/// Thrown when an <c>Idempotency-Key</c> is reused with a request body that differs from the
/// one originally cached under that key. The global exception middleware maps this to
/// <c>409 Conflict</c> with a Problem Details body.
/// </summary>
public sealed class IdempotencyKeyConflictException : Exception
{
    /// <summary>The <c>Idempotency-Key</c> header value that was reused.</summary>
    public string IdempotencyKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyKeyConflictException"/> class.
    /// </summary>
    /// <param name="idempotencyKey">The reused <c>Idempotency-Key</c> header value.</param>
    public IdempotencyKeyConflictException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' was already used with a different request body.")
    {
        IdempotencyKey = idempotencyKey;
    }
}
