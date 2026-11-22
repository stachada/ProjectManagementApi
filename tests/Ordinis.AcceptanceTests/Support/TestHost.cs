using Reqnroll;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// Owns the expensive, run-wide resources: one SQL Server container + in-process API host and
/// one webhook receiver, started once before the first scenario and disposed after the last.
/// Each scenario starts from an empty database (reset in <see cref="AfterScenario"/>).
/// </summary>
[Binding]
internal sealed class TestHost
{
    private static AcceptanceApiFactory? _factory;
    private static WebhookReceiver? _receiver;

    public static AcceptanceApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("The test host has not been started.");

    public static WebhookReceiver Receiver =>
        _receiver ?? throw new InvalidOperationException("The webhook receiver has not been started.");

    [BeforeTestRun]
    public static async Task StartAsync()
    {
        _receiver = new WebhookReceiver();
        await _receiver.StartAsync();

        _factory = new AcceptanceApiFactory(_receiver);
        await _factory.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task StopAsync()
    {
        if (_factory is not null)
        {
            await ((IAsyncLifetime)_factory).DisposeAsync();
        }

        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }
    }

    [AfterScenario(Order = int.MaxValue)]
    public static async Task AfterScenario()
    {
        await Factory.ResetDatabaseAsync();
        Receiver.Clear();
    }
}
