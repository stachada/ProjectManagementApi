using FluentValidation.TestHelper;
using Ordinis.Application.Users.Commands;

namespace Ordinis.UnitTests.Application.Users.Validators;

/// <summary>
/// Verifies <see cref="DeactivateUserValidator"/> rules. All synchronous -
/// no database state is involved.
/// </summary>
public sealed class DeactivateUserValidatorTests
{
    private static DeactivateUser ValidCommand(Guid? userId = null, Guid? requestedByUserId = null)
        => new(userId ?? Guid.CreateVersion7(), requestedByUserId ?? Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new DeactivateUserValidator();

        TestValidationResult<DeactivateUser> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUserId_HasValidationErrorForUserId()
    {
        var validator = new DeactivateUserValidator();

        TestValidationResult<DeactivateUser> result = await validator.TestValidateAsync(ValidCommand(userId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyRequestedByUserId_HasValidationErrorForRequestedByUserId()
    {
        var validator = new DeactivateUserValidator();

        TestValidationResult<DeactivateUser> result = await validator.TestValidateAsync(ValidCommand(requestedByUserId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }
}
