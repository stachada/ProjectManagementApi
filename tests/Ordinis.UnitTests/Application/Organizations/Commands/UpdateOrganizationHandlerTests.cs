using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Commands;

/// <summary>
/// Verifies <see cref="UpdateOrganizationHandler"/> updates name and description atomically -
/// one load, one <c>SaveChangesAsync</c> - unlike the old <c>RenameOrganization</c> +
/// <c>UpdateOrganizationDescription</c> split it replaces on <c>OrganizationsController.Update</c>.
/// </summary>
public class UpdateOrganizationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesNameAndDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(name: "Old Name", slug: "old-name", description: "Old description");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new UpdateOrganizationHandler(db)
            .HandleAsync(new UpdateOrganization(org.Id, "New Name", "New description"));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal("New Name", reloaded.Name);
        Assert.Equal("New description", reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_SlugRemainsUnchanged()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(name: "Old Name", slug: "old-name");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new UpdateOrganizationHandler(db)
            .HandleAsync(new UpdateOrganization(org.Id, "Completely Different Name", null));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal("old-name", reloaded.Slug);
    }

    [Fact]
    public async Task HandleAsync_NullDescription_ClearsDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(description: "Will be cleared");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new UpdateOrganizationHandler(db)
            .HandleAsync(new UpdateOrganization(org.Id, "New Name", null));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Null(reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new UpdateOrganizationHandler(db)
                .HandleAsync(new UpdateOrganization(Guid.CreateVersion7(), "New Name", null)));
    }

    [Fact]
    public async Task HandleAsync_SuspendedOrganization_ThrowsDomainExceptionAndDoesNotPersistPartialChange()
    {
        // Rename() calls EnsureActive() before UpdateDescription() ever runs, so a suspended
        // organization's name AND description both stay untouched - not just the name, unlike
        // the old two-command split where the first command's own success was already committed
        // by the time a later step could fail.
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(name: "Original Name", description: "Original description");
        org.Suspend();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => new UpdateOrganizationHandler(db)
                .HandleAsync(new UpdateOrganization(org.Id, "Any Name", "Any description")));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal("Original Name", reloaded.Name);
        Assert.Equal("Original description", reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        db.Entry(org).Property(o => o.RowVersion).OriginalValue = [1, 2, 3, 4, 5, 6, 7, 8];

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => new UpdateOrganizationHandler(db)
                .HandleAsync(new UpdateOrganization(org.Id, "New Name", null)));
    }
}
