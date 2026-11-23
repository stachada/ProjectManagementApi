using Microsoft.Extensions.Internal;

namespace Ordinis.Infrastructure.Common;

/// <summary>
/// Presents a <see cref="TimeProvider"/> as the <see cref="ISystemClock"/> that
/// <c>MemoryCacheOptions.Clock</c> expects, so in-memory cache expiry follows the app's clock
/// rather than reading the system clock directly.
/// </summary>
internal sealed class TimeProviderSystemClock(TimeProvider timeProvider) : ISystemClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
