namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// The test host's <see cref="TimeProvider"/>: the real clock plus an offset a scenario can move
/// forward ("Given 7 days and 1 second have passed"). Every host (and the seeder) reads "now"
/// from it, so token lifetimes, idempotency-key expiry and webhook retry schedules all see the
/// skipped time.
/// </summary>
/// <remarks>
/// Only <see cref="GetUtcNow"/> is shifted. Timers and delays still run in real time, so the
/// background jobs keep polling at their usual pace — they just find that more time has passed.
/// The offset is cleared after every scenario (<see cref="TestHost.AfterScenario"/>).
/// </remarks>
internal sealed class TestClock : TimeProvider
{
    private long _offsetTicks;

    /// <summary>How far ahead of the real clock this clock currently is.</summary>
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;

    /// <summary>Lets <paramref name="duration"/> pass at once.</summary>
    public void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Interlocked.Add(ref _offsetTicks, duration.Ticks);
    }

    /// <summary>Moves the clock forward to <paramref name="moment"/> (never backwards).</summary>
    public void AdvanceTo(DateTimeOffset moment)
    {
        TimeSpan remaining = moment - GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            Advance(remaining);
        }
    }

    /// <summary>Back to the real time.</summary>
    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}
