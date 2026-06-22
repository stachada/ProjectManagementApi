using Ordinis.Domain.Common;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Users;

/// <summary>
/// Verifies <see cref="User"/> aggregate invariants:
/// factory validation, profile mutations, refresh token handling,
/// and active/inactive state guards.
/// </summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    #region Create
    [Fact]
    public void Create_ValidArguments_UserIsActive()
    {
        User user = UserBuilder.Create(displayName: "  Jane Doe  ", email: "  Jane@Example.com  ");

        Assert.True(user.IsActive);
        Assert.Equal("Jane Doe", user.DisplayName);
        Assert.Equal("jane@example.com", user.Email);
        Assert.Equal(Role.Member, user.OrgRole);
    }

    [Fact]
    public void Create_ValidArguments_AssignsId()
    {
        User user = UserBuilder.Create();

        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public void Create_EmptyDisplayName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => UserBuilder.Create(displayName: " "));
    }

    [Fact]
    public void Create_EmptyEmail_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => UserBuilder.Create(email: " "));
    }

    [Fact]
    public void Create_EmptyPasswordHash_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => UserBuilder.Create(passwordHash: " "));
    }

    [Fact]
    public void Create_EmptyOrganizationId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => UserBuilder.Create(organizationId: Guid.Empty));
    }
    #endregion

    #region UpdateDisplayName
    [Fact]
    public void UpdateDisplayName_ValidName_UpdatesName()
    {
        User user = UserBuilder.Create();

        user.UpdateDisplayName("New Name");

        Assert.Equal("New Name", user.DisplayName);
    }

    [Fact]
    public void UpdateDisplayName_Empty_ThrowsArgumentException()
    {
        User user = UserBuilder.Create();

        Assert.Throws<ArgumentException>(() => user.UpdateDisplayName(" "));
    }
    #endregion

    #region ChangePasswordHash
    [Fact]
    public void ChangePasswordHash_ValidHash_UpdatesHash()
    {
        User user = UserBuilder.Create();

        user.ChangePasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePasswordHash_Empty_ThrowsArgumentException()
    {
        User user = UserBuilder.Create();

        Assert.Throws<ArgumentException>(() => user.ChangePasswordHash(" "));
    }
    #endregion

    #region Refresh token
    [Fact]
    public void SetRefreshToken_ValidToken_SetsTokenAndExpiry()
    {
        User user = UserBuilder.Create();
        DateTimeOffset expiresAt = Now.AddDays(7);

        user.SetRefreshToken("token-hash", expiresAt);

        Assert.Equal("token-hash", user.RefreshToken);
        Assert.Equal(expiresAt, user.RefreshTokenExpiresAt);
    }

    [Fact]
    public void SetRefreshToken_EmptyToken_ThrowsArgumentException()
    {
        User user = UserBuilder.Create();

        Assert.Throws<ArgumentException>(() => user.SetRefreshToken(" ", Now.AddDays(7)));
    }

    [Fact]
    public void RevokeRefreshToken_ClearsTokenAndExpiry()
    {
        User user = UserBuilder.Create();
        user.SetRefreshToken("token-hash", Now.AddDays(7));

        user.RevokeRefreshToken();

        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiresAt);
    }
    #endregion

    #region Deactivate / Reactivate
    [Fact]
    public void Deactivate_ActiveUser_SetsIsActiveFalse()
    {
        User user = UserBuilder.Create();

        user.Deactivate();

        Assert.False(user.IsActive);
    }

    [Fact]
    public void Deactivate_AlreadyInactive_ThrowsDomainException()
    {
        User user = UserBuilder.Create();
        user.Deactivate();

        DomainException ex = Assert.Throws<DomainException>(() => user.Deactivate());

        Assert.Equal("user.already-inactive", ex.ErrorCode);
    }

    [Fact]
    public void Reactivate_InactiveUser_SetsIsActiveTrue()
    {
        User user = UserBuilder.Create();
        user.Deactivate();

        user.Reactivate();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void Reactivate_AlreadyActive_ThrowsDomainException()
    {
        User user = UserBuilder.Create();

        DomainException ex = Assert.Throws<DomainException>(() => user.Reactivate());

        Assert.Equal("user.already-active", ex.ErrorCode);
    }
    #endregion

    #region ChangeOrgRole
    [Fact]
    public void ChangeOrgRole_NewRole_UpdatesRole()
    {
        User user = UserBuilder.Create(orgRole: Role.Member);

        user.ChangeOrgRole(Role.Admin);

        Assert.Equal(Role.Admin, user.OrgRole);
    }
    #endregion
}
