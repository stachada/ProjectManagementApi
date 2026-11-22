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

/// <summary>Outcome of <see cref="IIdempotencyStore.TryReserveAsync"/>.</summary>
public enum IdempotencyReservationStatus
{
    /// <summary>The key was free and is now reserved by the caller, which must later complete or release it.</summary>
    Reserved,

    /// <summary>Another request holding the same key is still being processed.</summary>
    InFlight,

    /// <summary>A request with the same key already completed successfully; its response is available for replay.</summary>
    Completed,
}

/// <summary>
/// Result of trying to reserve an idempotency key.
/// </summary>
/// <param name="Status">Whether the key was reserved, is in flight, or already completed.</param>
/// <param name="RequestBodyHash">
/// The body hash of the request that owns the key (the caller's own hash when <see cref="IdempotencyReservationStatus.Reserved"/>),
/// used to detect a key reused with a different payload.
/// </param>
/// <param name="Record">The stored response when <see cref="IdempotencyReservationStatus.Completed"/>; otherwise <see langword="null"/>.</param>
public sealed record IdempotencyReservation(
    IdempotencyReservationStatus Status,
    string RequestBodyHash,
    IdempotencyRecord? Record);

/// <summary>
/// Stores idempotent <c>POST</c> requests by <c>Idempotency-Key</c>: an atomic "in flight"
/// reservation while a request is processed, then its successful response for replay. Backing
/// storage is an infrastructure concern — see <c>InMemoryIdempotencyStore</c> in
/// <c>Ordinis.Infrastructure</c> for the current implementation.
/// </summary>
/// <remarks>
/// The reservation is what makes concurrent duplicates safe: without it, two identical requests
/// arriving together would both miss the cache and both execute (GAP-13). Implementations must
/// make <see cref="TryReserveAsync"/> atomic — a check followed by a separate write is not enough.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically reserves <paramref name="key"/> for the caller if it is free, or reports that
    /// it is in flight or already completed. A reservation expires after
    /// <paramref name="inFlightTtl"/>, so a request that never completes cannot block its key forever.
    /// </summary>
    Task<IdempotencyReservation> TryReserveAsync(
        string key, string requestBodyHash, TimeSpan inFlightTtl, CancellationToken cancellationToken);

    /// <summary>Replaces the caller's reservation with the successful response, kept for <paramref name="ttl"/>.</summary>
    Task CompleteAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Drops the caller's reservation without storing anything — after a failed request, so the
    /// client can retry with the same key. Has no effect if the key already completed.
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}
