using System.Globalization;
using System.Text.RegularExpressions;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Skipping time on the <see cref="TestHost.Clock"/> ("Given 7 days and 1 second have passed"):
/// it happens at once, and everything that reads the app's <see cref="TimeProvider"/> sees it.
/// </summary>
[Binding]
internal sealed partial class ClockSteps
{
    private const string Amount = @"\d+ (?:days?|hours?|minutes?|seconds?)";

    [Given("^(" + Amount + "(?: and " + Amount + ")*) (?:have|has) passed$")]
    public void GivenTimeHasPassed(string duration) => TestHost.Clock.Advance(Parse(duration));

    /// <summary>"7 days and 1 second" → 7.00:00:01.</summary>
    private static TimeSpan Parse(string duration)
    {
        TimeSpan total = TimeSpan.Zero;
        foreach (Match part in AmountPattern().Matches(duration))
        {
            int value = int.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture);
            total += part.Groups[2].Value switch
            {
                "day" => TimeSpan.FromDays(value),
                "hour" => TimeSpan.FromHours(value),
                "minute" => TimeSpan.FromMinutes(value),
                _ => TimeSpan.FromSeconds(value),
            };
        }

        return total;
    }

    [GeneratedRegex(@"(\d+) (day|hour|minute|second)s?")]
    private static partial Regex AmountPattern();
}
