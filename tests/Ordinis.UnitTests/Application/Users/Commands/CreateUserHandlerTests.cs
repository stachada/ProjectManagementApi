using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// Verifies <see cref="CreateUserHandler"/> hashes the password before persisting,
/// never passes plaintext to the domain, and returns the new user ID.
/// </summary>
public class CreateUserHandlerTests
{
    private static readonly FakePasswordHasher PasswordHasher = new();

    [Fact]
    public async Task HandleAsync_ValidCommand_CreatesUserWithCorrectFields()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateUserHandler(db, PasswordHasher);

        var orgId = Guid.CreateVersion7();
        var command = new CreateUser(
            OrganizationId: orgId,
            DisplayName: "Alice",
            Email: "alice@example.com",
            Password: "plaintext123",
            OrgRole: Role.Member);

        Guid userId = await handler.HandleAsync(command, CancellationToken.None);

        User reloaded = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal(orgId, reloaded.OrganizationId);
        Assert.Equal("Alice", reloaded.DisplayName);
        Assert.Equal("alice@example.com", reloaded.Email);
        Assert.Equal(Role.Member, reloaded.OrgRole);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PasswordHashedBeforeReachingDomain()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateUserHandler(db, PasswordHasher);

        Guid userId = await handler.HandleAsync(new CreateUser(
            OrganizationId: Guid.CreateVersion7(),
            DisplayName: "Bob",
            Email: "bob@example.com",
            Password: "secret",
            OrgRole: Role.Member), CancellationToken.None);

        User reloaded = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.NotEqual("secret", reloaded.PasswordHash);
        Assert.Equal(PasswordHasher.Hash("secret"), reloaded.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_ReturnsNewUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateUserHandler(db, PasswordHasher);

        Guid userId = await handler.HandleAsync(new CreateUser(
            OrganizationId: Guid.CreateVersion7(),
            DisplayName: "Carol",
            Email: "carol@example.com",
            Password: "pass1234"), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, userId);
        Assert.True(await db.Users.AnyAsync(u => u.Id == userId));
    }

    [Fact]
    public async Task HandleAsync_EmptyDisplayName_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateUserHandler(db, PasswordHasher);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new CreateUser(
                OrganizationId: Guid.CreateVersion7(),
                DisplayName: "",
                Email: "a@example.com",
                Password: "pass1234"), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyEmail_ThrowsArgumentException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var handler = new CreateUserHandler(db, PasswordHasher);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.HandleAsync(new CreateUser(
                OrganizationId: Guid.CreateVersion7(),
                DisplayName: "Dave",
                Email: "",
                Password: "pass1234"), CancellationToken.None));
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string plaintext) => $"hashed:{plaintext}";
        public bool Verify(string plaintext, string hash) => hash == $"hashed:{plaintext}";
    }
}
