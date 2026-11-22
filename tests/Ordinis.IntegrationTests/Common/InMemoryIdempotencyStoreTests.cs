using Microsoft.Extensions.Caching.Memory;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Common;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Verifies <see cref="InMemoryIdempotencyStore"/>'s reservation semantics (GAP-13). Like
/// <see cref="WebhookUrlGuardTests"/>, it needs no database or hosted app — it lives here only
/// because <c>Ordinis.UnitTests</c> cannot reference <c>Ordinis.Infrastructure</c>.
/// </summary>
public sealed class InMemoryIdempotencyStoreTests : IDisposable
{
    private static readonly TimeSpan InFlightTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly InMemoryIdempotencyStore _store;

    public InMemoryIdempotencyStoreTests() => _store = new InMemoryIdempotencyStore(_cache);

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task TryReserve_FreeKey_IsReserved()
    {
        IdempotencyReservation result = await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default);

        Assert.Equal(IdempotencyReservationStatus.Reserved, result.Status);
    }

    [Fact]
    public async Task TryReserve_KeyAlreadyReserved_IsInFlightWithOwnersHash()
    {
        await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default);

        IdempotencyReservation result = await _store.TryReserveAsync("k", "hash-b", InFlightTtl, default);

        Assert.Equal(IdempotencyReservationStatus.InFlight, result.Status);
        Assert.Equal("hash-a", result.RequestBodyHash);
    }

    [Fact]
    public async Task TryReserve_KeyCompleted_ReturnsStoredRecord()
    {
        await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default);
        var record = new IdempotencyRecord("hash-a", 201, "application/json", "/x/1", [1, 2, 3]);
        await _store.CompleteAsync("k", record, Ttl, default);

        IdempotencyReservation result = await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default);

        Assert.Equal(IdempotencyReservationStatus.Completed, result.Status);
        Assert.Same(record, result.Record);
    }

    [Fact]
    public async Task Release_Reservation_FreesTheKey()
    {
        await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default);

        await _store.ReleaseAsync("k", default);

        Assert.Equal(IdempotencyReservationStatus.Reserved, (await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default)).Status);
    }

    [Fact]
    public async Task Release_AfterCompletion_KeepsTheStoredResponse()
    {
        await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default);
        await _store.CompleteAsync("k", new IdempotencyRecord("hash-a", 201, null, null, []), Ttl, default);

        await _store.ReleaseAsync("k", default);

        Assert.Equal(IdempotencyReservationStatus.Completed, (await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default)).Status);
    }

    [Fact]
    public async Task TryReserve_ExpiredReservation_CanBeReservedAgain()
    {
        await _store.TryReserveAsync("k", "hash-a", TimeSpan.FromMilliseconds(50), default);
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(IdempotencyReservationStatus.Reserved, (await _store.TryReserveAsync("k", "hash-a", InFlightTtl, default)).Status);
    }

    [Fact]
    public async Task TryReserve_ManyConcurrentCallers_ExactlyOneReserves()
    {
        IdempotencyReservation[] results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => _store.TryReserveAsync("k", "hash-a", InFlightTtl, default))));

        Assert.Single(results, r => r.Status == IdempotencyReservationStatus.Reserved);
        Assert.Equal(63, results.Count(r => r.Status == IdempotencyReservationStatus.InFlight));
    }
}
