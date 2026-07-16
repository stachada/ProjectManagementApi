using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Infrastructure.Persistence;
using Respawn;
using Testcontainers.MsSql;

namespace Ordinis.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real <c>Ordinis.Api</c> host in-process against a disposable SQL Server
/// container, using the same <c>Ordinis.Infrastructure.Migrations.SqlServer</c> migrations
/// production runs — no InMemory/SQLite substitute, so RowVersion concurrency tokens and
/// provider-specific SQL behave exactly as they do in production.
/// </summary>
public sealed class OrdinisApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private Respawner? _respawner;

    /// <summary>
    /// Wipes all table data (but not schema) between tests via Respawn, so each test starts
    /// from a clean, migrated database without paying the cost of re-running migrations.
    /// In your tests, you Reset your database before each test run. If there are any tables/schemas
    /// that you don't want to be cleared out, include these in the configuration of your RespawnerOptions.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using SqlConnection connection = new(_sqlContainer.GetConnectionString());
        await connection.OpenAsync();

        // Respawn examines the SQL metadata intelligently to build a deterministic order of tables to delete
        // based on foreign key relationships between tables. It navigates these relationships to build a DELETE script
        // starting with the tables with no relationships and moving inwards until all tables are accounted for.

        // Once this in-order list of tables is created in the CreateAsync factory, the Respawner object keeps
        // this list of tables privately so that the list of tables and the order is only calculated once.
        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = ["__EFMigrationsHistory"],
        });

        await _respawner.ResetAsync(connection);
    }

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        // Program.cs calls AddInfrastructureServices(builder.Configuration) — which reads
        // DatabaseProvider/ConnectionStrings:DefaultConnection synchronously — BEFORE
        // builder.Build() runs. WebApplicationFactory's ConfigureWebHost hooks (including
        // ConfigureAppConfiguration) only attach once Build() is invoked, so by the time they'd
        // apply, AddInfrastructureServices has already thrown for the missing connection string.
        // Environment variables, by contrast, are read by WebApplication.CreateBuilder(args)
        // itself at the moment it runs, so setting them on the process before the host is ever
        // built (i.e. before Services/CreateClient is first touched) reaches Program.cs in time.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DatabaseProvider", "SqlServer");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _sqlContainer.GetConnectionString());

        // Force host creation now (rather than lazily on first client request) so migrations
        // are applied before any test issues an HTTP call.
        using IServiceScope scope = Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // The production global rate limiter (100 req/min per client IP) throttles
            // WebApplicationFactory's in-process TestServer client too, since every request
            // shares the same loopback partition key. Tests that specifically exercise rate
            // limiting should build their own factory/limiter config instead of relying on this
            // shared instance.
            services.Configure<RateLimiterOptions>(options => options.GlobalLimiter = null);

            // Lets tests deterministically force an optimistic-concurrency race via a
            // SaveChangesAsync barrier instead of racing real HTTP/DB timing - see
            // docs/INTEGRATION_TESTS.md and ConcurrencyRaceInterceptor's XML doc. EF Core does not
            // auto-attach interceptors merely because they're registered as ISaveChangesInterceptor
            // services - AddInterceptors must be called explicitly on the options builder, so a
            // second AddDbContext<AppDbContext> call layers this on top of the one already
            // registered in InfrastructureServiceExtensions (EF Core composes multiple
            // IDbContextOptionsConfiguration<TContext> registrations rather than replacing).
            services.AddSingleton<ConcurrencyRaceInterceptor>();
            services.AddDbContext<AppDbContext>((sp, options) =>
                options.AddInterceptors(sp.GetRequiredService<ConcurrencyRaceInterceptor>()));
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
