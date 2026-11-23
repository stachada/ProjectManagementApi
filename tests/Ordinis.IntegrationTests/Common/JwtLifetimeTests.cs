using Ordinis.Api.Common;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// <see cref="JwtLifetime"/> replaces IdentityModel's lifetime check so that it reads the app's
/// <see cref="TimeProvider"/>; these pin down that it keeps the same rules.
/// </summary>
public sealed class JwtLifetimeTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsValid_BeforeExpiry_ReturnsTrue() =>
        Assert.True(JwtLifetime.IsValid(Now.AddMinutes(-14), Now.AddSeconds(1), Now, TimeSpan.Zero));

    [Fact]
    public void IsValid_AtTheExactExpiry_ReturnsTrue() =>
        Assert.True(JwtLifetime.IsValid(Now.AddMinutes(-15), Now, Now, TimeSpan.Zero));

    [Fact]
    public void IsValid_AfterExpiry_ReturnsFalse() =>
        Assert.False(JwtLifetime.IsValid(Now.AddMinutes(-15), Now.AddSeconds(-1), Now, TimeSpan.Zero));

    [Fact]
    public void IsValid_WithoutAnExpiry_ReturnsFalse() =>
        Assert.False(JwtLifetime.IsValid(Now.AddMinutes(-1), expires: null, Now, TimeSpan.Zero));

    [Fact]
    public void IsValid_BeforeNotBefore_ReturnsFalse() =>
        Assert.False(JwtLifetime.IsValid(Now.AddSeconds(1), Now.AddMinutes(15), Now, TimeSpan.Zero));

    [Fact]
    public void IsValid_NotBeforeAfterExpiry_ReturnsFalse() =>
        Assert.False(JwtLifetime.IsValid(Now.AddMinutes(-1), Now.AddMinutes(-2), Now.AddMinutes(-3), TimeSpan.FromHours(1)));

    [Fact]
    public void IsValid_ClockSkewWidensBothBoundaries()
    {
        TimeSpan skew = TimeSpan.FromMinutes(5);

        Assert.True(JwtLifetime.IsValid(Now.AddMinutes(-20), Now.AddMinutes(-4), Now, skew));
        Assert.True(JwtLifetime.IsValid(Now.AddMinutes(4), Now.AddMinutes(20), Now, skew));
        Assert.False(JwtLifetime.IsValid(Now.AddMinutes(-20), Now.AddMinutes(-6), Now, skew));
    }

    [Fact]
    public void Validator_TakesNowFromTheTimeProvider()
    {
        var clock = new FixedClock(Now);
        var validator = JwtLifetime.Validator(clock);
        var parameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters { ClockSkew = TimeSpan.Zero };
        DateTime expires = Now.AddMinutes(15);

        Assert.True(validator(Now, expires, null, parameters));

        clock.Now = Now.AddMinutes(15).AddSeconds(1);
        Assert.False(validator(Now, expires, null, parameters));
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
