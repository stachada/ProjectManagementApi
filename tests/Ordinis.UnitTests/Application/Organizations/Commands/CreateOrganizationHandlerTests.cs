using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Organizations.Commands;

/// <summary>
/// Verifies <see cref="CreateOrganizationHandler"/> persists a new organization,
/// auto-generates the slug, and returns the new ID.
/// </summary>
public class CreateOrganizationHandlerTests
{
    private static readonly ISlugGenerator SlugGenerator = new SlugGenerator();

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesOrganizationWithCorrectFields()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateOrganizationHandler(db, SlugGenerator);

        var command = new CreateOrganization(
            Name: "Acme Corp",
            Description: "The finest products in the land");

        Guid orgId = await handler.HandleAsync(command);

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == orgId);

        Assert.Equal("Acme Corp", reloaded.Name);
        Assert.Equal("acme-corp", reloaded.Slug);
        Assert.Equal("The finest products in the land", reloaded.Description);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_ReturnsNewOrganizationId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateOrganizationHandler(db, SlugGenerator);

        Guid orgId = await handler.HandleAsync(new CreateOrganization("Test Org"));

        Assert.NotEqual(Guid.Empty, orgId);
        Assert.True(await db.Organizations.AnyAsync(o => o.Id == orgId));
    }

    [Fact]
    public async Task HandleAsync_NullDescription_CreatesOrganizationWithNullDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateOrganizationHandler(db, SlugGenerator);

        Guid orgId = await handler.HandleAsync(new CreateOrganization(Name: "No Desc Org"));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == orgId);
        Assert.Null(reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateOrganizationHandler(db, SlugGenerator);

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.HandleAsync(new CreateOrganization(Name: "")));
    }
}
