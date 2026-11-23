using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// A Serilog sink that keeps every event the API logs, registered in the test host's services
/// and picked up by Program.cs's <c>ReadFrom.Services</c> — so scenarios check the real logging
/// pipeline (configuration, enrichers, scopes), not a copy of it. Cleared after every scenario.
/// </summary>
internal sealed class CapturedLogs : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public void Clear() => _events.Clear();
}
