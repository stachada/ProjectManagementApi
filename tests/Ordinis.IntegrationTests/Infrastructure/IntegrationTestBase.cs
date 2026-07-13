using Microsoft.Extensions.DependencyInjection;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;
using Ordinis.Infrastructure.Persistence;

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

    /// <summary>
    /// Opens a scope, resolves <c>AppDbContext</c>, runs <paramref name="seed"/> against it, and
    /// disposes the scope - the boilerplate every seeding helper otherwise repeats.
    /// </summary>
    protected async Task<TResult> SeedAsync<TResult>(Func<AppDbContext, Task<TResult>> seed)
    {
        using IServiceScope scope = CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await seed(db);
    }

    /// <summary>
    /// Builds an unsaved <see cref="Organization"/> with a globally-unique slug
    /// (<c>Organization.Slug</c> has a unique DB index) - callers still add it to
    /// <c>db.Organizations</c> and call <c>SaveChangesAsync</c>.
    /// </summary>
    protected static Organization CreateOrganization(string name = "Acme") =>
        Organization.Create(name, $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.CreateVersion7()}");

    /// <summary>
    /// Seeds a fresh <see cref="Organization"/> plus one <see cref="User"/> in it - the
    /// prerequisite pair nearly every controller test needs before it can seed anything more
    /// specific (a project, a board, a task). Each call creates its own organization (globally
    /// unique slug via <see cref="CreateOrganization"/>), so the email is never at risk of
    /// colliding with another call even without its own random suffix.
    /// </summary>
    protected Task<(Guid OrganizationId, Guid UserId)> SeedOrganizationWithUserAsync(
        string displayName = "Alice", string email = "alice@example.com") => SeedAsync(async db =>
    {
        Organization org = CreateOrganization();
        db.Organizations.Add(org);

        User user = User.Create(org.Id, displayName, email, "hashed-password");
        db.Users.Add(user);

        await db.SaveChangesAsync();

        return (org.Id, user.Id);
    });

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Factory.ResetDatabaseAsync();
}
