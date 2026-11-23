using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Protocol phrases that can end any action step — "… without stating a version",
/// "… with request key "k-1"", "… using API version 2" / "… in API v2", "… with correlation
/// id "x"". Each one records how the next request is to be altered
/// (<see cref="World.NextRequest"/>) and then runs the step it wraps, so every action supports
/// every phrase without a dedicated variant of each step.
/// </summary>
/// <remarks>
/// The driver applies the alteration to the first request sent, so a wrapped step must make its
/// main request first (versions come from the database, not from a preliminary read).
/// </remarks>
[Binding]
internal sealed class RequestModifierSteps(World world, ITestRunner runner)
{
    [When(@"^(.+) without stating a version$")]
    public Task WhenWithoutAVersion(string step)
    {
        world.NextRequest.OmitIfMatch = true;
        return RunAsync(step);
    }

    [When(@"^(.+) with request key ""([^""]+)""$")]
    public Task WhenWithARequestKey(string step, string key)
    {
        world.NextRequest.IdempotencyKey = key;
        return RunAsync(step);
    }

    [When(@"^(.+) (?:using API version |in API v)(\d+)$")]
    public Task WhenUsingAnApiVersion(string step, int version)
    {
        world.NextRequest.ApiVersion = version;
        return RunAsync(step);
    }

    [When(@"^(.+) with correlation id ""([^""]+)""$")]
    public Task WhenWithACorrelationId(string step, string correlationId)
    {
        world.NextRequest.CorrelationId = correlationId;
        return RunAsync(step);
    }

    private Task RunAsync(string step) => runner.WhenAsync(step, null, null, "When ");
}
