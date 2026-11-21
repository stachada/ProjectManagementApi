using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Ordinis.Benchmarks.Infrastructure;

/// <summary>
/// Job configuration for I/O-bound benchmarks (database round-trips) where the interesting
/// numbers are per-request latency percentiles, not the throughput-oriented mean BenchmarkDotNet
/// optimizes for by default.
/// </summary>
/// <remarks>
/// <para>
/// BenchmarkDotNet's statistics are computed over <em>iterations</em>, and by default each
/// iteration batches many invocations and reports their average - which smooths away exactly
/// the tail a P99 is meant to expose. Pinning <c>InvocationCount = 1</c> /
/// <c>UnrollFactor = 1</c> makes every measurement a single query execution, so
/// <c>P50</c>/<c>P95</c>/<c>P99</c> describe real per-call latency.
/// </para>
/// <para>
/// The cost: BenchmarkDotNet warns that iterations are shorter than its 100&#160;ms
/// <c>MinIterationTime</c> heuristic. That heuristic protects CPU-bound microbenchmarks from
/// timer resolution error; at multi-millisecond database latencies it doesn't apply.
/// </para>
/// </remarks>
internal sealed class PerOperationLatencyConfig : ManualConfig
{
    public PerOperationLatencyConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(20)
            .WithIterationCount(200)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithId("PerOperation"));

        AddColumn(StatisticColumn.Median, StatisticColumn.P95, new PercentileColumn(99));
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
