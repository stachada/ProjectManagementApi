namespace Ordinis.Application.Common;

/// <summary>
/// A cached response for a previously handled idempotent request, keyed by its
/// <c>Idempotency-Key</c> header value (scoped per route by the caller).
/// </summary>
/// <param name="RequestBodyHash">
/// SHA-256 hash (hex) of the original request body — compared against a replay request's body
/// hash to detect the same key being reused with a different payload.
/// </param>
/// <param name="StatusCode">The original response's HTTP status code.</param>
/// <param name="ContentType">The original response's <c>Content-Type</c> header, if any.</param>
/// <param name="Location">The original response's <c>Location</c> header, if any.</param>
/// <param name="Body">The original response body bytes.</param>
public sealed record IdempotencyRecord(
    string RequestBodyHash,
    int StatusCode,
    string? ContentType,
    string? Location,
    byte[] Body);

/// <summary>
/// Stores and retrieves cached responses for idempotent <c>POST</c> requests, keyed by
/// <c>Idempotency-Key</c>. Backing storage is an infrastructure concern — see
/// <c>InMemoryIdempotencyStore</c> in <c>Ordinis.Infrastructure</c> for the current
/// implementation.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Looks up a previously cached response for <paramref name="key"/>, if any.</summary>
    Task<IdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Caches <paramref name="record"/> under <paramref name="key"/> for <paramref name="ttl"/>.</summary>
    Task SetAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken);
}
