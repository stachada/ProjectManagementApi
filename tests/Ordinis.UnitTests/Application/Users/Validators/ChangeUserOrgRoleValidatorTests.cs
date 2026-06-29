using FluentValidation.TestHelper;
using Ordinis.Application.Users.Commands;
using Ordinis.Domain.Users;

namespace Ordinis.UnitTests.Application.Users.Validators;

/// <summary>
/// Verifies <see cref="ChangeUserOrgRoleValidator"/> rules. All synchronous -
/// no database state is involved.
/// </summary>
public sealed class ChangeUserOrgRoleValidatorTests
{
    private static ChangeUserOrgRole ValidCommand(Guid? userId = null, Role newOrgRole = Role.Admin, Guid? requestedByUserId = null)
        => new(userId ?? Guid.CreateVersion7(), newOrgRole, requestedByUserId ?? Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new ChangeUserOrgRoleValidator();

        TestValidationResult<ChangeUserOrgRole> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUserId_HasValidationErrorForUserId()
    {
        var validator = new ChangeUserOrgRoleValidator();

        TestValidationResult<ChangeUserOrgRole> result = await validator.TestValidateAsync(ValidCommand(userId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public async Task TestValidateAsync_NewOrgRoleNotAValidEnumValue_HasValidationErrorForNewOrgRole()
    {
        var validator = new ChangeUserOrgRoleValidator();

        TestValidationResult<ChangeUserOrgRole> result = await validator.TestValidateAsync(ValidCommand(newOrgRole: (Role)99));

        result.ShouldHaveValidationErrorFor(x => x.NewOrgRole);
    }

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Member)]
    [InlineData(Role.Admin)]
    public async Task TestValidateAsync_NewOrgRoleIsValidEnumValue_HasNoValidationErrorForNewOrgRole(Role role)
    {
        var validator = new ChangeUserOrgRoleValidator();

        TestValidationResult<ChangeUserOrgRole> result = await validator.TestValidateAsync(ValidCommand(newOrgRole: role));

        result.ShouldNotHaveValidationErrorFor(x => x.NewOrgRole);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyRequestedByUserId_HasValidationErrorForRequestedByUserId()
    {
        var validator = new ChangeUserOrgRoleValidator();

        TestValidationResult<ChangeUserOrgRole> result = await validator.TestValidateAsync(ValidCommand(requestedByUserId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }
}
