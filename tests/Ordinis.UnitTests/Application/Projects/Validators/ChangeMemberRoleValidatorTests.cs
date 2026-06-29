using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Users;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="ChangeMemberRoleValidator"/> rules. All synchronous -
/// no database state is involved.
/// </summary>
public sealed class ChangeMemberRoleValidatorTests
{
    private static ChangeMemberRole ValidCommand(Guid? projectId = null, Guid? userId = null, Role newRole = Role.Admin)
        => new(projectId ?? Guid.CreateVersion7(), userId ?? Guid.CreateVersion7(), newRole);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new ChangeMemberRoleValidator();

        TestValidationResult<ChangeMemberRole> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyProjectId_HasValidationErrorForProjectId()
    {
        var validator = new ChangeMemberRoleValidator();

        TestValidationResult<ChangeMemberRole> result = await validator.TestValidateAsync(ValidCommand(projectId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUserId_HasValidationErrorForUserId()
    {
        var validator = new ChangeMemberRoleValidator();

        TestValidationResult<ChangeMemberRole> result = await validator.TestValidateAsync(ValidCommand(userId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public async Task TestValidateAsync_NewRoleNotAValidEnumValue_HasValidationErrorForNewRole()
    {
        var validator = new ChangeMemberRoleValidator();

        TestValidationResult<ChangeMemberRole> result = await validator.TestValidateAsync(ValidCommand(newRole: (Role)99));

        result.ShouldHaveValidationErrorFor(x => x.NewRole)
            .WithErrorMessage("Invalid role value.");
    }

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Member)]
    [InlineData(Role.Admin)]
    public async Task TestValidateAsync_NewRoleIsValidEnumValue_HasNoValidationErrorForNewRole(Role role)
    {
        var validator = new ChangeMemberRoleValidator();

        TestValidationResult<ChangeMemberRole> result = await validator.TestValidateAsync(ValidCommand(newRole: role));

        result.ShouldNotHaveValidationErrorFor(x => x.NewRole);
    }
}
