using Ordinis.Application.Common;
using Ordinis.Application.Users.Dtos;
using Ordinis.Application.Users.Queries;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Queries;

/// <summary>
/// Verifies <see cref="GetUserByIdHandler"/> returns the correct <see cref="UserDto"/>,
/// resolves the organization name, excludes auth-sensitive fields, and throws when not found.
/// </summary>
public class GetUserByIdHandlerTests
{
    [Fact]
    public async Task HandleAsync_ValidQuery_ReturnsCorrectDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        var org = OrganizationBuilder.Create(name: "Acme Corp");
        db.Organizations.Add(org);

        User user = UserBuilder.Create(
            organizationId: org.Id,
            displayName: "Alice",
            email: "alice@example.com",
            orgRole: Role.Admin);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        UserDto dto = await new GetUserByIdHandler(db)
            .HandleAsync(new GetUserById(user.Id), CancellationToken.None);

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal("Alice", dto.DisplayName);
        Assert.Equal("alice@example.com", dto.Email);
        Assert.Equal(org.Id, dto.OrganizationId);
        Assert.Equal("Acme Corp", dto.OrganizationName);
        Assert.Equal("Admin", dto.OrgRole); // OrgRole is serialized as string in UserDto
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task HandleAsync_AuthSensitiveFieldsAbsentFromDto()
    {
        // Verifies that PasswordHash, RefreshToken, RefreshTokenExpiresAt are
        // never re-introduced on UserDto — fails the moment any of those reappear.
        var sensitiveNames = new[] { "PasswordHash", "RefreshToken", "RefreshTokenExpiresAt" };
        var dtoProperties = typeof(UserDto).GetProperties().Select(p => p.Name);

        foreach (var sensitive in sensitiveNames)
        {
            Assert.DoesNotContain(sensitive, dtoProperties);
        }
    }

    [Fact]
    public async Task HandleAsync_OrganizationMissing_OrganizationNameFallsBackToEmpty()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create(organizationId: Guid.CreateVersion7());
        db.Users.Add(user);
        await db.SaveChangesAsync();

        UserDto dto = await new GetUserByIdHandler(db)
            .HandleAsync(new GetUserById(user.Id), CancellationToken.None);

        Assert.Equal(string.Empty, dto.OrganizationName);
    }

    [Fact]
    public async Task HandleAsync_DeactivatedUser_IsActiveFalseInDto()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        user.Deactivate();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        UserDto dto = await new GetUserByIdHandler(db)
            .HandleAsync(new GetUserById(user.Id), CancellationToken.None);

        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task HandleAsync_UnknownUserId_ThrowsNotFoundException()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(
            () => new GetUserByIdHandler(db)
                .HandleAsync(new GetUserById(Guid.CreateVersion7()), CancellationToken.None));
    }
}
