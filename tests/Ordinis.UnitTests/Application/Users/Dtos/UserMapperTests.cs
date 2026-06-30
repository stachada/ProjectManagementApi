using System.Reflection;
using Ordinis.Application.Users.Dtos;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Dtos;

/// <summary>
/// Verifies <see cref="UserMapper"/> field mapping. Pure function test -
/// no EF Core, no DI, no async.
/// </summary>
public class UserMapperTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToDto_AllFieldsMapCorrectly()
    {
        var organizationId = Guid.CreateVersion7();
        User user = UserBuilder.Create(
            organizationId: organizationId,
            displayName: "Ada Lovelace",
            email: "ada@example.com",
            orgRole: Role.Admin);
        user.CreatedAt = Now;
        user.UpdatedAt = Now.AddHours(1);

        UserDto dto = user.ToDto("Acme Corp");

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal("Ada Lovelace", dto.DisplayName);
        Assert.Equal("ada@example.com", dto.Email);
        Assert.Equal("Admin", dto.OrgRole);
        Assert.True(dto.IsActive);
        Assert.Equal(organizationId, dto.OrganizationId);
        Assert.Equal("Acme Corp", dto.OrganizationName);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
    }

    [Fact]
    public void ToDto_OrganizationNameParameterFlowsThrough()
    {
        User user = UserBuilder.Create();

        UserDto first = user.ToDto("Org One");
        UserDto second = user.ToDto("Org Two");

        Assert.Equal("Org One", first.OrganizationName);
        Assert.Equal("Org Two", second.OrganizationName);
    }

    [Fact]
    public void ToDto_DeactivatedUser_IsActiveIsFalse()
    {
        User user = UserBuilder.Create();
        user.Deactivate();

        UserDto dto = user.ToDto("Acme Corp");

        Assert.False(dto.IsActive);
    }

    [Fact]
    public void ToDto_AuthSensitiveFieldsAreAbsentFromTheDto()
    {
        // Guards against ever re-introducing these onto the public DTO -
        // checked structurally rather than via an instance so the test
        // fails even if the fields are added but left unmapped.
        PropertyInfo[] dtoProperties = typeof(UserDto).GetProperties();

        Assert.DoesNotContain(dtoProperties, p => p.Name == "PasswordHash");
        Assert.DoesNotContain(dtoProperties, p => p.Name == "RefreshToken");
        Assert.DoesNotContain(dtoProperties, p => p.Name == "RefreshTokenExpiresAt");
    }
}
