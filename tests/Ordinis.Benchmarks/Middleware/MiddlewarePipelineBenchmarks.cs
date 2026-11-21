using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ordinis.Api.Common;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ordinis.Benchmarks.Middleware;

/// <summary>
/// Which of the API's cross-cutting middlewares sit in front of the endpoint.
/// Cumulative, in <c>Program.cs</c> order, so each step isolates one middleware's cost.
/// </summary>
public enum PipelineStage
{
    /// <summary>Routing + endpoint only.</summary>
    Bare,

    /// <summary>+ <see cref="CorrelationIdMiddleware"/>.</summary>
    CorrelationId,

    /// <summary>+ <see cref="RequestLoggingMiddleware"/>.</summary>
    CorrelationIdAndLogging,

    /// <summary>+ <see cref="GlobalExceptionMiddleware"/> - the full observability/error stack.</summary>
    Full,
}

/// <summary>
/// Per-request overhead of the correlation ID, request logging and exception handling
/// middlewares, measured as an in-memory request/response round-trip through
/// <see cref="TestServer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TestServer"/> skips Kestrel and the network entirely, so the remaining cost is the
/// ASP.NET Core pipeline itself - the right baseline for attributing overhead to middleware.
/// Absolute numbers are lower than a real socket round-trip; the <em>deltas</em> between stages
/// are what this benchmark is for.
/// </para>
/// <para>
/// Logging goes through real Serilog (same enrichment and the same <c>Microsoft.AspNetCore</c>
/// <c>Warning</c> override as <c>appsettings.json</c>) into a sink that discards events, so
/// message-template parsing, property capture and <c>LogContext</c> enrichment are measured but
/// console I/O - which depends on the terminal, not the code - is not.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class MiddlewarePipelineBenchmarks
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>Which middlewares are installed for this benchmark case.</summary>
    [Params(PipelineStage.Bare, PipelineStage.CorrelationId, PipelineStage.CorrelationIdAndLogging, PipelineStage.Full)]
    public PipelineStage Stage { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });
        builder.WebHost.UseTestServer();

        Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DiscardingSink())
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(serilog, dispose: true);

        _app = builder.Build();

        if (Stage >= PipelineStage.CorrelationId)
        {
            _app.UseMiddleware<CorrelationIdMiddleware>();
        }

        if (Stage >= PipelineStage.CorrelationIdAndLogging)
        {
            _app.UseMiddleware<RequestLoggingMiddleware>();
        }

        if (Stage >= PipelineStage.Full)
        {
            _app.UseMiddleware<GlobalExceptionMiddleware>();
        }

        _app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>One <c>GET /ping</c> round-trip through the configured pipeline.</summary>
    [Benchmark]
    public async Task<int> GetPing()
    {
        using HttpResponseMessage response = await _client.GetAsync("/ping");
        response.EnsureSuccessStatusCode();
        return (int)response.StatusCode;
    }

    /// <summary>Serilog sink that accepts and drops every event.</summary>
    private sealed class DiscardingSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
        }
    }
}
