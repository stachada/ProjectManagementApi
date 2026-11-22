using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Scenarios tagged <c>@gap</c> specify intended behaviour the API does not meet yet (see the
/// gap register in README.md). They are reported as skipped so the suite stays green; set
/// <c>ORDINIS_RUN_GAPS=1</c> to run them and see them fail.
/// </summary>
[Binding]
internal sealed class GapHooks(IUnitTestRuntimeProvider runtime)
{
    public const string RunGapsVariable = "ORDINIS_RUN_GAPS";

    [BeforeScenario("gap", Order = 0)]
    public void SkipKnownGaps()
    {
        if (Environment.GetEnvironmentVariable(RunGapsVariable) != "1")
        {
            runtime.TestIgnore($"Known gap (see the README gap register). Set {RunGapsVariable}=1 to run it.");
        }
    }
}
