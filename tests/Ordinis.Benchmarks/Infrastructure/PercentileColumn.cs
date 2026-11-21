using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Ordinis.Benchmarks.Infrastructure;

/// <summary>
/// Summary-table column reporting an arbitrary percentile of a benchmark's measurements.
/// </summary>
/// <remarks>
/// BenchmarkDotNet ships fixed percentile columns (<c>P0</c>, <c>P25</c>, <c>P50</c>, <c>P67</c>,
/// <c>P80</c>, <c>P85</c>, <c>P90</c>, <c>P95</c>, <c>P100</c>) but no <c>P99</c>, which is the
/// tail-latency figure the query benchmarks are asked to report. Percentiles are computed over
/// the per-iteration measurements, so they only describe <em>per-operation</em> latency when the
/// job runs one invocation per iteration - see <see cref="PerOperationLatencyConfig"/>.
/// </remarks>
/// <param name="percentile">The percentile to report, 0-100.</param>
internal sealed class PercentileColumn(int percentile) : IColumn
{
    public string Id => $"{nameof(PercentileColumn)}.P{percentile}";

    public string ColumnName => $"P{percentile}";

    public bool AlwaysShow => true;

    public ColumnCategory Category => ColumnCategory.Statistics;

    public int PriorityInCategory => 100 + percentile;

    public bool IsNumeric => true;

    public UnitType UnitType => UnitType.Time;

    public string Legend => $"{percentile}th percentile of all measurements";

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public bool IsAvailable(Summary summary) => true;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        => GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        BenchmarkDotNet.Mathematics.Statistics? statistics = summary[benchmarkCase]?.ResultStatistics;
        if (statistics is null)
        {
            return "NA";
        }

        double nanoseconds = statistics.Percentiles.Percentile(percentile);
        return FormatNanoseconds(nanoseconds, style.CultureInfo);
    }

    public override string ToString() => ColumnName;

    // Formatted independently of BenchmarkDotNet's internal unit-selection helpers (which have
    // changed shape between releases) - the same ns/us/ms/s thresholds its own columns use.
    private static string FormatNanoseconds(double nanoseconds, CultureInfo culture)
        => nanoseconds switch
        {
            < 1_000 => string.Create(culture, $"{nanoseconds:N2} ns"),
            < 1_000_000 => string.Create(culture, $"{nanoseconds / 1_000:N2} us"),
            < 1_000_000_000 => string.Create(culture, $"{nanoseconds / 1_000_000:N2} ms"),
            _ => string.Create(culture, $"{nanoseconds / 1_000_000_000:N2} s"),
        };
}
