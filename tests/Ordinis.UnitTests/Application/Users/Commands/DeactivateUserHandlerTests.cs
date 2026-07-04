using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// Verifies <see cref="DeactivateUserHandler"/> sets <c>IsActive = false</c>
/// and that domain guards are enforced.
/// </summary>
public class DeactivateUserHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_DeactivatesUser()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new DeactivateUserHandler(db).HandleAsync(
            new DeactivateUser(user.Id, Guid.CreateVersion7()), CancellationToken.None);

        User reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(reloaded.IsActive);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new DeactivateUserHandler(db).HandleAsync(
                new DeactivateUser(Guid.CreateVersion7(), Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AlreadyInactive_ThrowsDomainException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        user.Deactivate();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => new DeactivateUserHandler(db).HandleAsync(
                new DeactivateUser(user.Id, Guid.CreateVersion7()),
                CancellationToken.None));
    }
}
