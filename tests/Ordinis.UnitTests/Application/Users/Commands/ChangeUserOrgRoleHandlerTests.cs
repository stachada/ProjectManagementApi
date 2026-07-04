using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// Verifies <see cref="ChangeUserOrgRoleHandler"/> updates the org role and
/// translates concurrency conflicts correctly.
/// </summary>
public class ChangeUserOrgRoleHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_ChangesOrgRole()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(orgRole: Role.Member);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new ChangeUserOrgRoleHandler(db).HandleAsync(
            new ChangeUserOrgRole(user.Id, Role.Admin, Guid.CreateVersion7()),
            CancellationToken.None);

        User reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(Role.Admin, reloaded.OrgRole);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new ChangeUserOrgRoleHandler(db).HandleAsync(
                new ChangeUserOrgRole(Guid.CreateVersion7(), Role.Viewer, Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.Entry(user).Property(u => u.RowVersion).OriginalValue = [1, 2, 3, 4, 5, 6, 7, 8];

        await Assert.ThrowsAsync<ConcurrencyException>(
            () => new ChangeUserOrgRoleHandler(db).HandleAsync(
                new ChangeUserOrgRole(user.Id, Role.Admin, Guid.CreateVersion7()),
                CancellationToken.None));
    }
}
