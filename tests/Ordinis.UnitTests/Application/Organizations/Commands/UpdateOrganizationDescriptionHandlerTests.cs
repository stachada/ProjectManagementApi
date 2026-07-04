using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Commands;

/// <summary>
/// Verifies <see cref="UpdateOrganizationDescriptionHandler"/> updates the description,
/// clears it when null, and translates concurrency conflicts correctly.
/// </summary>
public class UpdateOrganizationDescriptionHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(description: "Old description");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new UpdateOrganizationDescriptionHandler(db)
            .HandleAsync(new UpdateOrganizationDescription(org.Id, "Brand new description"));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Equal("Brand new description", reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_NullDescription_ClearsDescription()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(description: "Will be cleared");
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new UpdateOrganizationDescriptionHandler(db)
            .HandleAsync(new UpdateOrganizationDescription(org.Id, null));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.Null(reloaded.Description);
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new UpdateOrganizationDescriptionHandler(db)
                .HandleAsync(new UpdateOrganizationDescription(Guid.CreateVersion7(), "Any")));
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
            () => new UpdateOrganizationDescriptionHandler(db)
                .HandleAsync(new UpdateOrganizationDescription(org.Id, "Any")));
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
            () => new UpdateOrganizationDescriptionHandler(db)
                .HandleAsync(new UpdateOrganizationDescription(org.Id, "New description")));
    }
}
