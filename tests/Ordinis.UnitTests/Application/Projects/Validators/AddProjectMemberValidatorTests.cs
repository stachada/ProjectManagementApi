using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="AddProjectMemberValidator"/> rules, including the async
/// project-existence, user-existence, and not-already-a-member checks run
/// against the database.
/// </summary>
public sealed class AddProjectMemberValidatorTests
{
    private static AddProjectMember ValidCommand(Guid projectId, Guid userId, Role role = Role.Member)
        => new(projectId, userId, role);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyProjectId_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty, user.Id));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public async Task TestValidateAsync_ProjectDoesNotExist_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7(), user.Id));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId)
            .WithErrorMessage("Project not found.");
    }

    [Fact]
    public async Task TestValidateAsync_ProjectExists_HasNoValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUserId_HasValidationErrorForUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public async Task TestValidateAsync_UserDoesNotExist_HasValidationErrorForUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.UserId)
            .WithErrorMessage("User not found.");
    }

    [Fact]
    public async Task TestValidateAsync_UserExists_HasNoValidationErrorForUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public async Task TestValidateAsync_RoleNotAValidEnumValue_HasValidationErrorForRole()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id, (Role)99));

        result.ShouldHaveValidationErrorFor(x => x.Role)
            .WithErrorMessage("Invalid role value.");
    }

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Member)]
    [InlineData(Role.Admin)]
    public async Task TestValidateAsync_RoleIsValidEnumValue_HasNoValidationErrorForRole(Role role)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id, role));

        result.ShouldNotHaveValidationErrorFor(x => x.Role);
    }

    [Fact]
    public async Task TestValidateAsync_UserAlreadyAMember_HasValidationErrorForUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        db.ProjectMembers.Add(ProjectMember.Create(project.Id, user.Id, Role.Member, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldHaveValidationErrorFor(x => x.UserId)
            .WithErrorMessage("User is already a member of the project.");
    }

    [Fact]
    public async Task TestValidateAsync_UserNotYetAMember_HasNoValidationErrorForUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        User user = UserBuilder.Create();
        db.Projects.Add(project);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddProjectMemberValidator(db);

        TestValidationResult<AddProjectMember> result = await validator.TestValidateAsync(ValidCommand(project.Id, user.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.UserId);
    }
}
