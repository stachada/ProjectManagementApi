using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Migrations.SqlServer.DesignTime;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> to create <see cref="AppDbContext"/>
/// against the SQL Server provider, independent of <c>Ordinis.Api/Program.cs</c>.
/// </summary>
/// <remarks>
/// Only this assembly's migrations are generated/applied for SQL Server — see
/// <c>MigrationsAssembly</c> below and in <c>InfrastructureServiceExtensions.AddDatabase</c>.
/// Resolves <c>ConnectionStrings:DefaultConnection</c> from the same sources
/// <c>Ordinis.Api</c> uses at runtime — <c>Ordinis.Api</c>'s User Secrets (by ID, since this
/// project doesn't own that secrets ID) and environment variables (e.g.
/// <c>ConnectionStrings__DefaultConnection</c> in CI/Docker) — so <c>dotnet ef database update</c>
/// works against a real database without an explicit <c>--connection</c> override whenever those
/// are already configured. Falls back to a syntactically valid but unreachable connection string
/// when neither source has a value, so <c>migrations add</c> still works offline in a fresh clone.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string OrdinisApiUserSecretsId = "7d722b01-cc54-4c3d-9fa1-d572c2451f7e";

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(OrdinisApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString =
                "Server=localhost;Database=OrdinisDesignTime;Trusted_Connection=True;TrustServerCertificate=True;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseSqlServer(
            connectionString,
            sql => sql.MigrationsAssembly("Ordinis.Infrastructure.Migrations.SqlServer"));

        return new AppDbContext(optionsBuilder.Options, TimeProvider.System);
    }
}
