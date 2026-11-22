using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Common;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Users.Commands;

/// <summary>
/// GAP-05: an organization must always keep at least one active org Admin, so neither
/// <see cref="ChangeUserOrgRoleHandler"/> nor <see cref="DeactivateUserHandler"/> may remove the
/// last one (enforced by <c>OrganizationAdminGuard</c>).
/// </summary>
public sealed class LastOrgAdminRuleTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    [Theory]
    [InlineData(Role.Member)]
    [InlineData(Role.Viewer)]
    public async Task Demote_LastActiveAdmin_ThrowsAndKeepsRole(Role newRole)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);

        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ChangeUserOrgRoleHandler(db).HandleAsync(new ChangeUserOrgRole(admin.Id, newRole, admin.Id), CancellationToken.None));

        Assert.Equal("organization.last-admin-demotion", ex.ErrorCode);
        Assert.Equal(Role.Admin, (await db.Users.FindAsync(admin.Id))!.OrgRole);
    }

    [Fact]
    public async Task Demote_AdminWhenAnotherActiveAdminExists_Succeeds()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);
        await AddUserAsync(db, "second-admin@acme.example", Role.Admin);

        await new ChangeUserOrgRoleHandler(db).HandleAsync(new ChangeUserOrgRole(admin.Id, Role.Member, admin.Id), CancellationToken.None);

        Assert.Equal(Role.Member, (await db.Users.FindAsync(admin.Id))!.OrgRole);
    }

    [Fact]
    public async Task Demote_LastActiveAdmin_WhenOtherAdminIsInactive_Throws()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);
        User inactiveAdmin = await AddUserAsync(db, "former-admin@acme.example", Role.Admin);
        inactiveAdmin.Deactivate();
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new ChangeUserOrgRoleHandler(db).HandleAsync(new ChangeUserOrgRole(admin.Id, Role.Member, admin.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Demote_LastActiveAdmin_AnotherOrganizationsAdminDoesNotCount()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);
        db.Users.Add(UserBuilder.Create(organizationId: Guid.CreateVersion7(), email: "admin@globex.example", orgRole: Role.Admin));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new ChangeUserOrgRoleHandler(db).HandleAsync(new ChangeUserOrgRole(admin.Id, Role.Member, admin.Id), CancellationToken.None));
    }

    [Fact]
    public async Task KeepAdminRole_LastActiveAdmin_IsNotADemotion()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);

        await new ChangeUserOrgRoleHandler(db).HandleAsync(new ChangeUserOrgRole(admin.Id, Role.Admin, admin.Id), CancellationToken.None);

        Assert.Equal(Role.Admin, (await db.Users.FindAsync(admin.Id))!.OrgRole);
    }

    [Fact]
    public async Task Deactivate_LastActiveAdmin_ThrowsAndKeepsUserActive()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);

        DomainException ex = await Assert.ThrowsAsync<DomainException>(() =>
            new DeactivateUserHandler(db).HandleAsync(new DeactivateUser(admin.Id, admin.Id), CancellationToken.None));

        Assert.Equal("organization.last-admin-deactivation", ex.ErrorCode);
        Assert.True((await db.Users.FindAsync(admin.Id))!.IsActive);
    }

    [Fact]
    public async Task Deactivate_AdminWhenAnotherActiveAdminExists_Succeeds()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User admin = await AddUserAsync(db, "admin@acme.example", Role.Admin);
        await AddUserAsync(db, "second-admin@acme.example", Role.Admin);

        await new DeactivateUserHandler(db).HandleAsync(new DeactivateUser(admin.Id, admin.Id), CancellationToken.None);

        Assert.False((await db.Users.FindAsync(admin.Id))!.IsActive);
    }

    [Fact]
    public async Task Deactivate_NonAdmin_IsUnaffectedByTheRule()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        await AddUserAsync(db, "admin@acme.example", Role.Admin);
        User member = await AddUserAsync(db, "member@acme.example", Role.Member);

        await new DeactivateUserHandler(db).HandleAsync(new DeactivateUser(member.Id, member.Id), CancellationToken.None);

        Assert.False((await db.Users.FindAsync(member.Id))!.IsActive);
    }

    private static async Task<User> AddUserAsync(TestAppDbContext db, string email, Role orgRole)
    {
        User user = UserBuilder.Create(organizationId: OrganizationId, email: email, orgRole: orgRole);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
