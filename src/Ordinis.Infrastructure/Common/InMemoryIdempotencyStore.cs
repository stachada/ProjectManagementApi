using Microsoft.Extensions.Caching.Memory;
using Ordinis.Application.Common;

namespace Ordinis.Infrastructure.Common;

/// <summary>
/// <see cref="IIdempotencyStore"/> implementation backed by <see cref="IMemoryCache"/>.
/// Single-instance only — a future multi-instance deployment would swap this for a
/// distributed-cache-backed implementation without touching <c>IdempotencyMiddleware</c> or
/// any controller.
/// </summary>
/// <param name="cache">The shared in-memory cache instance.</param>
public sealed class InMemoryIdempotencyStore(IMemoryCache cache) : IIdempotencyStore
{
    private readonly IMemoryCache _cache = cache;

    /// <inheritdoc />
    public Task<IdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        _cache.TryGetValue(key, out IdempotencyRecord? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken)
    {
        _cache.Set(key, record, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        return Task.CompletedTask;
    }
}
