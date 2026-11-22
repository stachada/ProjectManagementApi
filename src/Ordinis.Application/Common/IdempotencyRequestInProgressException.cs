namespace Ordinis.Application.Common;

/// <summary>
/// Thrown when a request arrives with an <c>Idempotency-Key</c> whose original request (same
/// body) is still being processed. The global exception middleware maps this to
/// <c>409 Conflict</c> with a <c>Retry-After</c> header; once the original completes, a retry
/// with the same key replays its response.
/// </summary>
public sealed class IdempotencyRequestInProgressException : Exception
{
    /// <summary>The <c>Idempotency-Key</c> header value that is in flight.</summary>
    public string IdempotencyKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyRequestInProgressException"/> class.
    /// </summary>
    /// <param name="idempotencyKey">The in-flight <c>Idempotency-Key</c> header value.</param>
    public IdempotencyRequestInProgressException(string idempotencyKey)
        : base($"A request with idempotency key '{idempotencyKey}' is still being processed. Retry shortly to receive its result.")
    {
        IdempotencyKey = idempotencyKey;
    }
}
