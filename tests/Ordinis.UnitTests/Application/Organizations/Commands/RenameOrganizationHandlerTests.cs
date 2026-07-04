using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Commands;

/// <summary>
/// Verifies <see cref="RenameOrganizationHandler"/> updates the organization name,
/// leaves the slug immutable, and translates concurrency conflicts correctly.
/// </summary>
public class RenameOrganizationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(name: "Old Name", slug: "old-name");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new RenameOrganizationHandler(db)
            .HandleAsync(new RenameOrganization(org.Id, "New Name"));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal("New Name", reloaded.Name);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_SlugRemainsUnchanged()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(name: "Old Name", slug: "old-name");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new RenameOrganizationHandler(db)
            .HandleAsync(new RenameOrganization(org.Id, "Completely Different Name"));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal("old-name", reloaded.Slug);
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new RenameOrganizationHandler(db)
                .HandleAsync(new RenameOrganization(Guid.CreateVersion7(), "New Name")));
    }

    [Fact]
    public async Task HandleAsync_SuspendedOrganization_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        org.Suspend();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => new RenameOrganizationHandler(db)
                .HandleAsync(new RenameOrganization(org.Id, "Any Name")));
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
            () => new RenameOrganizationHandler(db)
                .HandleAsync(new RenameOrganization(org.Id, "New Name")));
    }
}
