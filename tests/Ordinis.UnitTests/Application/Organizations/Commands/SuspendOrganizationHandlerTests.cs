using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Commands;

/// <summary>
/// Verifies <see cref="SuspendOrganizationHandler"/> sets <c>IsActive = false</c>
/// and that domain guards are enforced.
/// </summary>
public class SuspendOrganizationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_SuspendsOrganization()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new SuspendOrganizationHandler(db)
            .HandleAsync(new SuspendOrganization(org.Id));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new SuspendOrganizationHandler(db)
                .HandleAsync(new SuspendOrganization(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_AlreadySuspended_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        org.Suspend();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => new SuspendOrganizationHandler(db)
                .HandleAsync(new SuspendOrganization(org.Id)));
    }
}
