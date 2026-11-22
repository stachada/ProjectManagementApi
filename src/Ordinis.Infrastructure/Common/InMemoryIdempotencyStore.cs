using Microsoft.Extensions.Caching.Memory;
using Ordinis.Application.Common;

namespace Ordinis.Infrastructure.Common;

/// <summary>
/// <see cref="IIdempotencyStore"/> implementation backed by <see cref="IMemoryCache"/>.
/// Single-instance only — a future multi-instance deployment would swap this for a
/// database- or distributed-cache-backed implementation (with an atomic insert as the
/// reservation) without touching <c>IdempotencyMiddleware</c> or any controller.
/// </summary>
/// <remarks>
/// <see cref="IMemoryCache"/> has no atomic "add if absent", so check-and-set runs under one
/// lock. The critical sections are a dictionary lookup and a write — never the request itself —
/// so contention is negligible.
/// </remarks>
/// <param name="cache">The shared in-memory cache instance.</param>
public sealed class InMemoryIdempotencyStore(IMemoryCache cache) : IIdempotencyStore
{
    private readonly IMemoryCache _cache = cache;
    private readonly Lock _gate = new();

    /// <summary>Placeholder stored under a key while its request is being processed.</summary>
    private sealed record InFlightMarker(string RequestBodyHash);

    /// <inheritdoc />
    public Task<IdempotencyReservation> TryReserveAsync(
        string key, string requestBodyHash, TimeSpan inFlightTtl, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out object? entry))
            {
                IdempotencyReservation existing = entry switch
                {
                    IdempotencyRecord record => new(IdempotencyReservationStatus.Completed, record.RequestBodyHash, record),
                    InFlightMarker marker => new(IdempotencyReservationStatus.InFlight, marker.RequestBodyHash, null),
                    _ => throw new InvalidOperationException($"Unexpected idempotency cache entry type {entry?.GetType()}."),
                };
                return Task.FromResult(existing);
            }

            _cache.Set(key, new InFlightMarker(requestBodyHash),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = inFlightTtl });

            return Task.FromResult(new IdempotencyReservation(IdempotencyReservationStatus.Reserved, requestBodyHash, null));
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _cache.Set(key, record, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // Only drop a reservation; never a completed response.
            if (_cache.TryGetValue(key, out object? entry) && entry is InFlightMarker)
            {
                _cache.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}
