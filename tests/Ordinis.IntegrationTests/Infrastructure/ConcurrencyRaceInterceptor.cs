using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ordinis.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only <see cref="ISaveChangesInterceptor"/>, registered as a singleton in
/// <see cref="OrdinisApiFactory"/>, that deterministically forces an optimistic-concurrency race:
/// once <see cref="Arm"/> is called, the next <c>SaveChangesAsync</c> call across ANY
/// <c>AppDbContext</c> instance pauses immediately before executing its UPDATE, until
/// <see cref="ReleaseFirst"/> is called. This lets a test load-then-pause one request, let a
/// second request load-modify-save the same entity to completion, and only then release the
/// first - guaranteeing the first request's save observes a stale RowVersion and gets a genuine
/// <see cref="DbUpdateConcurrencyException"/>, with no dependency on real network/DB timing. See
/// docs/INTEGRATION_TESTS.md for the full rationale and a sequence diagram.
/// </summary>
public sealed class ConcurrencyRaceInterceptor : SaveChangesInterceptor
{
    private TaskCompletionSource? _firstArrived;
    private TaskCompletionSource? _releaseFirst;
    private int _state; // 0 = disarmed, 1 = armed (awaiting first arrival), 2 = first arrival consumed

    /// <summary>Arms the barrier so the next <c>SaveChangesAsync</c> call pauses.</summary>
    public void Arm()
    {
        _firstArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _state, 1);
    }

    /// <summary>Completes once the paused save call has reached the barrier.</summary>
    public Task WaitForFirstArrivalAsync() => _firstArrived!.Task;

    /// <summary>Releases the paused save call so it can proceed to execute its UPDATE.</summary>
    public void ReleaseFirst() => _releaseFirst!.SetResult(); // unblocks the first request so it can complete its save, which will now observe a stale RowVersion and throw a concurrency exception

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 1) == 1) // first request has arrived at the barrier, consume it and pause until the test releases it
        {
            _firstArrived!.SetResult(); // signal the test that the first request has reached the barrier and is paused
            await _releaseFirst!.Task; // pause the first request until the test releases it, so the second request can complete first
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
