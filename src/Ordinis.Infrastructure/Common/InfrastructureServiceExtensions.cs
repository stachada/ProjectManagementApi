using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.FileStorage;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Common;

/// <summary>
/// Registers all Infrastructure-layer services with the DI container.
/// Called from <c>Ordinis.Api/Program.cs</c>.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Adds EF Core DbContext (dual SQL Server / PostgreSQL provider), TimeProvider,
    /// local file storage, health checks, and the Outbox background dispatcher.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration (appsettings + secrets).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // AddDatabase validates and normalizes the provider string to its canonical casing
        // ("SqlServer" or "PostgreSQL") before returning it. Storing the canonical form in
        // OutboxOptions lets FetchBatchAsync's case-sensitive switch always match correctly,
        // even when the config value uses non-canonical casing such as "sqlserver".
        var provider = AddDatabase(services, configuration);

        services.Configure<OutboxOptions>(o => o.DatabaseProvider = provider);

        services.AddSingleton(TimeProvider.System);

        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        services.AddHostedService<OutboxDispatcherJob>();

        return services;
    }

    /// <returns>
    /// The canonical provider string (<c>"SqlServer"</c> or <c>"PostgreSQL"</c>),
    /// validated and normalized for use in <see cref="OutboxOptions"/>.
    /// </returns>
    private static string AddDatabase(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Use the IConfiguration indexer rather than GetValue<string> to avoid depending on
        // Microsoft.Extensions.Configuration.Binder, which is only available transitively
        // through EF Core packages and would break silently if those transitive deps change.
        var rawProvider = configuration["DatabaseProvider"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key 'DatabaseProvider'. " +
                "Set it to 'SqlServer' or 'PostgreSQL' in appsettings.json or environment variables.");

        // FIX: validate and normalize the provider string EAGERLY here, before AddDbContext.
        // The original code threw inside the options lambda, which only runs lazily on first
        // DbContext resolution — an invalid value like "MySql" silently passed startup and
        // only failed on the first HTTP request or outbox poll tick, with no startup signal.
        // Normalization to canonical casing also ensures FetchBatchAsync's case-sensitive
        // switch matches for config values like "sqlserver" (lowercase), which would otherwise
        // pass this validation (case-insensitive) but hit the switch's throw arm at runtime.
        string provider;
        if (rawProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            provider = "PostgreSQL";
        }
        else if (rawProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            provider = "SqlServer";
        }
        else
        {
            throw new InvalidOperationException(
                $"Unrecognized DatabaseProvider '{rawProvider}'. Valid values: 'SqlServer', 'PostgreSQL'.");
        }

        // FIX: GetConnectionString("key") returns "" (not null) when the key exists in
        // appsettings.json with an empty value. The base appsettings.json ships with
        // "DefaultConnection": "" as a placeholder, so ?? alone accepts the empty string and
        // defers the failure to first DB access with a cryptic provider exception instead of
        // a clear startup error. IsNullOrEmpty catches both the absent-key and empty-value cases.
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Missing required configuration key 'ConnectionStrings:DefaultConnection'. " +
                "Set it via User Secrets (development) or environment variables (production).");
        }

        // Provider is already validated and normalized above; the lambda needs no else-throw.
        // Using a plain if/else makes it explicit that only two paths are reachable here.
        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider == "PostgreSQL")
            {
                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        // Handlers inject IAppDbContext; forward to the same scoped AppDbContext instance.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return provider;
    }
}
