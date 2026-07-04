using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// Verifies <see cref="ReactivateUserHandler"/> sets <c>IsActive = true</c>
/// and that domain guards are enforced.
/// </summary>
public class ReactivateUserHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_ReactivatesUser()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        user.Deactivate();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new ReactivateUserHandler(db).HandleAsync(
            new ReactivateUser(user.Id, Guid.CreateVersion7()), CancellationToken.None);

        User reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new ReactivateUserHandler(db).HandleAsync(
                new ReactivateUser(Guid.CreateVersion7(), Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AlreadyActive_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(); // created active
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => new ReactivateUserHandler(db).HandleAsync(
                new ReactivateUser(user.Id, Guid.CreateVersion7()),
                CancellationToken.None));
    }
}
