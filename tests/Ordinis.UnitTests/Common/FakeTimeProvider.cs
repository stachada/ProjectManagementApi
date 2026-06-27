namespace Ordinis.UnitTests.Common;

/// <summary>
/// Returns a fixed instant for every call, so handler tests can assert on exact timestamps.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
