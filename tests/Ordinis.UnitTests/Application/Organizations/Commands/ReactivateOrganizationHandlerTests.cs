using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Organizations.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Organizations;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Organizations.Commands;

/// <summary>
/// Verifies <see cref="ReactivateOrganizationHandler"/> sets <c>IsActive = true</c>
/// and that domain guards are enforced.
/// </summary>
public class ReactivateOrganizationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_ReactivatesOrganization()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create();
        org.Suspend();
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await new ReactivateOrganizationHandler(db)
            .HandleAsync(new ReactivateOrganization(org.Id));

        Organization reloaded = await db.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task HandleAsync_UnknownOrganizationId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new ReactivateOrganizationHandler(db)
                .HandleAsync(new ReactivateOrganization(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task HandleAsync_AlreadyActive_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Organization org = OrganizationBuilder.Create(); // created active
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => new ReactivateOrganizationHandler(db)
                .HandleAsync(new ReactivateOrganization(org.Id)));
    }
}
