using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Migrations.SqlServer.DesignTime;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> to create <see cref="AppDbContext"/>
/// against the SQL Server provider, independent of <c>Ordinis.Api/Program.cs</c>.
/// </summary>
/// <remarks>
/// Only this assembly's migrations are generated/applied for SQL Server — see
/// <c>MigrationsAssembly</c> below and in <c>InfrastructureServiceExtensions.AddDatabase</c>.
/// The connection string here is never used to connect; EF only needs a syntactically valid
/// one to build the provider-specific model for schema generation.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=OrdinisDesignTime;Trusted_Connection=True;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly("Ordinis.Infrastructure.Migrations.SqlServer"));

        return new AppDbContext(optionsBuilder.Options, TimeProvider.System);
    }
}
