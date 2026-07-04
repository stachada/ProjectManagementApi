using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// Verifies <see cref="UpdateUserHandler"/> updates the display name and
/// translates concurrency conflicts correctly.
/// </summary>
public class UpdateUserHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesDisplayName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(displayName: "Old Name");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await new UpdateUserHandler(db).HandleAsync(
            new UpdateUser(user.Id, "New Name", Guid.CreateVersion7()),
            CancellationToken.None);

        User reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("New Name", reloaded.DisplayName);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new UpdateUserHandler(db).HandleAsync(
                new UpdateUser(Guid.CreateVersion7(), "Name", Guid.CreateVersion7()),
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyDisplayName_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => new UpdateUserHandler(db).HandleAsync(
                new UpdateUser(user.Id, "", Guid.CreateVersion7()),
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
            () => new UpdateUserHandler(db).HandleAsync(
                new UpdateUser(user.Id, "Updated Name", Guid.CreateVersion7()),
                CancellationToken.None));
    }
}
