using Ordinis.Domain.Common;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Projects;

/// <summary>
/// Verifies <see cref="Project"/> aggregate invariants:
/// membership rules, last-Admin protection, and archived-project guards.
/// </summary>
public sealed class ProjectTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    #region Create
    [Fact]
    public void Create_ValidArguments_CreatorIsAdminMember()
    {
        var creatorId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: creatorId, now: Now);

        ProjectMember member = Assert.Single(project.Members);
        Assert.Equal(creatorId, member.UserId);
        Assert.Equal(Role.Admin, member.Role);
    }
    #endregion

    #region AddMember / RemoveMember
    [Fact]
    public void AddMember_NewUser_AddsMemberWithCorrectRole()
    {
        Project project = ProjectBuilder.Create(now: Now);
        var newUserId = Guid.CreateVersion7();

        project.AddMember(newUserId, Role.Member, Now);

        Assert.Contains(project.Members, m => m.UserId == newUserId && m.Role == Role.Member);
    }

    [Fact]
    public void AddMember_DuplicateUser_ThrowsDomainException()
    {
        var userId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: userId, now: Now);

        DomainException ex = Assert.Throws<DomainException>(() =>
            project.AddMember(userId, Role.Member, Now));

        Assert.Equal("project.member-already-exists", ex.ErrorCode);
    }

    [Fact]
    public void AddMember_ArchivedProject_ThrowsDomainException()
    {
        Project project = ProjectBuilder.Create(now: Now);
        project.Archive();

        Assert.Throws<DomainException>(() =>
            project.AddMember(Guid.CreateVersion7(), Role.Member, Now));
    }

    [Fact]
    public void RemoveMember_NonAdminMember_RemovesMember()
    {
        Project project = ProjectBuilder.Create(now: Now);
        var memberId = Guid.CreateVersion7();
        project.AddMember(memberId, Role.Member, Now);

        project.RemoveMember(memberId);

        Assert.DoesNotContain(project.Members, m => m.UserId == memberId);
    }

    [Fact]
    public void RemoveMember_LastAdmin_ThrowsDomainException()
    {
        var creatorId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: creatorId, now: Now);

        DomainException ex = Assert.Throws<DomainException>(() =>
            project.RemoveMember(creatorId));

        Assert.Equal("project.last-admin-removal", ex.ErrorCode);
    }
    #endregion

    #region ChangeMemberRole
    [Fact]
    public void ChangeMemberRole_DemoteLastAdmin_ThrowsDomainException()
    {
        var creatorId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: creatorId, now: Now);

        DomainException ex = Assert.Throws<DomainException>(() =>
            project.ChangeMemberRole(creatorId, Role.Member));

        Assert.Equal("project.last-admin-demotion", ex.ErrorCode);
    }

    [Fact]
    public void ChangeMemberRole_WhenAnotherAdminExists_DemotionSucceeds()
    {
        var creatorId = Guid.CreateVersion7();
        var secondAdminId = Guid.CreateVersion7();
        Project project = ProjectBuilder.Create(createdByUserId: creatorId, now: Now);
        project.AddMember(secondAdminId, Role.Admin, Now);

        project.ChangeMemberRole(creatorId, Role.Member);

        ProjectMember member = project.Members.Single(m => m.UserId == creatorId);
        Assert.Equal(Role.Member, member.Role);
    }
    #endregion

    #region Archive / Unarchive
    [Fact]
    public void Archive_ActiveProject_SetsIsArchivedTrue()
    {
        Project project = ProjectBuilder.Create(now: Now);

        project.Archive();

        Assert.True(project.IsArchived);
    }

    [Fact]
    public void Rename_ArchivedProject_ThrowsDomainException()
    {
        Project project = ProjectBuilder.Create(now: Now);
        project.Archive();

        Assert.Throws<DomainException>(() => project.Rename("New Name"));
    }

    [Fact]
    public void Unarchive_ArchivedProject_SetsIsArchivedFalse()
    {
        Project project = ProjectBuilder.Create(now: Now);
        project.Archive();

        project.Unarchive();

        Assert.False(project.IsArchived);
    }
    #endregion
}
