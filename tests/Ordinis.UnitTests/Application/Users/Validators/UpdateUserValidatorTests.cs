using FluentValidation.TestHelper;
using Ordinis.Application.Users.Commands;

namespace Ordinis.UnitTests.Application.Users.Validators;

/// <summary>
/// Verifies <see cref="UpdateUserValidator"/> rules. All synchronous - no
/// database state is involved.
/// </summary>
public sealed class UpdateUserValidatorTests
{
    private static UpdateUser ValidCommand(Guid? userId = null, string displayName = "New Name", Guid? requestedByUserId = null)
        => new(userId ?? Guid.CreateVersion7(), displayName, requestedByUserId ?? Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new UpdateUserValidator();

        TestValidationResult<UpdateUser> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUserId_HasValidationErrorForUserId()
    {
        var validator = new UpdateUserValidator();

        TestValidationResult<UpdateUser> result = await validator.TestValidateAsync(ValidCommand(userId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_DisplayNameEmptyOrWhitespace_HasValidationErrorForDisplayName(string displayName)
    {
        var validator = new UpdateUserValidator();

        TestValidationResult<UpdateUser> result = await validator.TestValidateAsync(ValidCommand(displayName: displayName));

        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public async Task TestValidateAsync_DisplayNameExceedsMaxLength_HasValidationErrorForDisplayName()
    {
        var validator = new UpdateUserValidator();

        TestValidationResult<UpdateUser> result = await validator.TestValidateAsync(ValidCommand(displayName: new string('a', 101)));

        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public async Task TestValidateAsync_DisplayNameAtMaxLength_HasNoValidationErrorForDisplayName()
    {
        var validator = new UpdateUserValidator();

        TestValidationResult<UpdateUser> result = await validator.TestValidateAsync(ValidCommand(displayName: new string('a', 100)));

        result.ShouldNotHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyRequestedByUserId_HasValidationErrorForRequestedByUserId()
    {
        var validator = new UpdateUserValidator();

        TestValidationResult<UpdateUser> result = await validator.TestValidateAsync(ValidCommand(requestedByUserId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }
}
