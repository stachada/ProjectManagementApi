using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Common;
using Ordinis.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Ordinis.Benchmarks.Infrastructure;

/// <summary>
/// Locates (or starts) the SQL Server instance the database benchmarks and the load-test seeder
/// run against, and builds a DI container wired exactly like production's.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Connection resolution.</strong> If <see cref="ConnectionStringVariable"/> is set, it
/// is used as-is - point it at the docker-compose SQL Server (or any other instance) to benchmark
/// a specific environment or to share a seeded database with the API for load testing. Otherwise
/// a SQL Server Testcontainer is started, the same image the integration tests use.
/// </para>
/// <para>
/// <strong>Why a reusable container.</strong> BenchmarkDotNet runs every benchmark case in its
/// own child process, so a container tied to one process's lifetime would be started, migrated
/// and seeded with 10&#160;000 tasks once per case. <c>WithReuse(true)</c> lets every child
/// process attach to the one container the first case started; it is deliberately left running
/// afterwards (remove it with <c>docker rm -f ordinis-benchmarks-mssql</c>).
/// </para>
/// </remarks>
internal static class BenchmarkDatabase
{
    /// <summary>
    /// Environment variable holding an explicit SQL Server connection string to use instead of a
    /// Testcontainer.
    /// </summary>
    public const string ConnectionStringVariable = "ORDINIS_BENCHMARK_CONNECTION";

    private const string ContainerName = "ordinis-benchmarks-mssql";
    private const string DatabaseName = "OrdinisBenchmarks";

    /// <summary>
    /// Returns a connection string to a migrated, seeded benchmark database.
    /// </summary>
    public static async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithName(ContainerName)
            .WithReuse(true)
            .Build();

        await container.StartAsync(cancellationToken);

        // MsSqlBuilder always targets "master"; the dedicated database is created by
        // MigrateAsync in PrepareAsync (SQL Server's EF provider creates a missing database).
        return new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;
    }

    /// <summary>
    /// Builds a service provider with the real Application and Infrastructure registrations
    /// (dispatcher, handlers, validators, <see cref="AppDbContext"/>, password hasher) against
    /// <paramref name="connectionString"/>.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="currentUser">
    /// Caller identity for query handlers; <see langword="null"/> keeps the HTTP-claims-backed
    /// registration (only valid for code paths that never read it, such as seeding).
    /// </param>
    public static ServiceProvider BuildServices(string connectionString, ICurrentUserService? currentUser = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "SqlServer",
                ["ConnectionStrings:DefaultConnection"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);

        if (currentUser is not null)
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddSingleton(currentUser);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Applies migrations and seeds the benchmark dataset if it isn't there yet.
    /// Safe to call from every benchmark process - seeding is skipped once the dataset exists.
    /// </summary>
    public static async Task<SeededDataset> PrepareAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using ServiceProvider services = BuildServices(connectionString);

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }

        return await BenchmarkDataSeeder.SeedAsync(services, cancellationToken);
    }
}
