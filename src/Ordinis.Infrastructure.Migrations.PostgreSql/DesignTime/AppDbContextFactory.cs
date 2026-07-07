using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ordinis.Infrastructure.Persistence;

namespace Ordinis.Infrastructure.Migrations.PostgreSql.DesignTime;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> to create <see cref="AppDbContext"/>
/// against the PostgreSQL provider, independent of <c>Ordinis.Api/Program.cs</c>.
/// </summary>
/// <remarks>
/// Only this assembly's migrations are generated/applied for PostgreSQL — see
/// <c>MigrationsAssembly</c> below and in <c>InfrastructureServiceExtensions.AddDatabase</c>.
/// The connection string here is never used to connect; EF only needs a syntactically valid
/// one to build the provider-specific model for schema generation.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=ordinis_designtime;Username=postgres;Password=postgres",
            npg => npg.MigrationsAssembly("Ordinis.Infrastructure.Migrations.PostgreSql"));

        return new AppDbContext(optionsBuilder.Options, TimeProvider.System);
    }
}
