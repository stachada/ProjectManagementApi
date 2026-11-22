using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Organizations;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// Verifies <see cref="LoginHandler"/> credential matching. EF Core InMemory compares strings
/// case-sensitively — like PostgreSQL's default collation and unlike SQL Server's — so these tests
/// catch lookups that only work thanks to a case-insensitive database (GAP-16), which the SQL
/// Server integration suite cannot.
/// </summary>
public sealed class LoginHandlerTests
{
    private const string Password = "correct-horse-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("acme", "nora@acme.example")]           // exactly as stored
    [InlineData("acme", "NORA@ACME.EXAMPLE")]           // email case differs
    [InlineData("acme", "Nora@Acme.Example")]
    [InlineData("ACME", "nora@acme.example")]           // slug case differs
    [InlineData("  acme ", "  nora@acme.example  ")]    // surrounding whitespace
    public async Task HandleAsync_CredentialsMatchIgnoringCaseAndWhitespace_ReturnsTokens(string slug, string email)
    {
        using TestAppDbContext db = await SeedAsync();
        LoginHandler handler = CreateHandler(db);

        LoginResult result = await handler.HandleAsync(new Login(slug, email, Password), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
    }

    [Theory]
    [InlineData("acme", "nora@acme.example", "wrong-password")]
    [InlineData("acme", "ghost@acme.example", Password)]
    [InlineData("globex", "nora@acme.example", Password)]
    public async Task HandleAsync_WrongCredentials_ThrowsAuthenticationException(string slug, string email, string password)
    {
        using TestAppDbContext db = await SeedAsync();
        LoginHandler handler = CreateHandler(db);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => handler.HandleAsync(new Login(slug, email, password), CancellationToken.None));
    }

    private static async Task<TestAppDbContext> SeedAsync()
    {
        TestAppDbContext db = TestDbContextFactory.Create();
        Organization acme = OrganizationBuilder.Create(name: "Acme", slug: "acme");
        Organization globex = OrganizationBuilder.Create(name: "Globex", slug: "globex");
        db.Organizations.AddRange(acme, globex);
        db.Users.Add(UserBuilder.Create(
            organizationId: acme.Id,
            displayName: "Nora",
            email: "nora@acme.example",
            passwordHash: FakePasswordHasher.HashOf(Password)));
        await db.SaveChangesAsync();
        return db;
    }

    private static LoginHandler CreateHandler(TestAppDbContext db) =>
        new(db, new FakePasswordHasher(), new FakeJwtTokenService(), new FakeTimeProvider(Now));

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public static string HashOf(string plaintext) => $"hashed:{plaintext}";

        public string Hash(string plaintext) => HashOf(plaintext);

        public bool Verify(string plaintext, string hash) => hash == HashOf(plaintext);
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user, DateTimeOffset now) =>
            ("access-token", now.AddMinutes(15));

        public (string RefreshToken, DateTimeOffset ExpiresAt) GenerateRefreshToken(DateTimeOffset now) =>
            ("refresh-token", now.AddDays(7));

        public string HashRefreshToken(string refreshToken) => $"sha256:{refreshToken}";
    }
}
