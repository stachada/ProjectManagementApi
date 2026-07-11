using Microsoft.Extensions.DependencyInjection;

namespace Ordinis.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for API-level integration tests. Shares one <see cref="OrdinisApiFactory"/> (and
/// its SQL Server container) across the whole <see cref="ApiCollection"/>, and resets table data
/// after every test so tests remain independent regardless of execution order.
/// </summary>
[Collection(ApiCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IntegrationTestBase(OrdinisApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected OrdinisApiFactory Factory { get; }

    protected HttpClient Client { get; }

    /// <summary>Opens a DI scope for seeding data directly via <c>AppDbContext</c> before a request is made.</summary>
    protected IServiceScope CreateScope() => Factory.Services.CreateScope();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Factory.ResetDatabaseAsync();
}
