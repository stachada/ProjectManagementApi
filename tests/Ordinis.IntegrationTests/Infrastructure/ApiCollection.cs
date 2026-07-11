namespace Ordinis.IntegrationTests.Infrastructure;

/// <summary>
/// Shares a single <see cref="OrdinisApiFactory"/> (and its SQL Server container) across every
/// test class in the collection — starting a fresh container per test class would dominate the
/// run time, since container startup + migrations take seconds while a Respawn reset (see
/// <see cref="IntegrationTestBase"/>) takes milliseconds.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<OrdinisApiFactory>
{
    public const string Name = "Ordinis API";
}
