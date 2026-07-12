using FluentValidation.TestHelper;
using Ordinis.Application.Organizations.Commands;

namespace Ordinis.UnitTests.Application.Organizations.Validators;

/// <summary>
/// Verifies <see cref="UpdateOrganizationValidator"/> rules. All synchronous -
/// no database state is involved.
/// </summary>
public sealed class UpdateOrganizationValidatorTests
{
    private static UpdateOrganization ValidCommand(
        Guid? organizationId = null, string newName = "New Name", string? newDescription = "Updated description")
        => new(organizationId ?? Guid.CreateVersion7(), newName, newDescription);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_NullDescription_HasNoValidationErrors()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newDescription: null));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyOrganizationId_HasValidationErrorForOrganizationId()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(organizationId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NewNameEmptyOrWhitespace_HasValidationErrorForNewName(string newName)
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: newName));

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameExceedsMaxLength_HasValidationErrorForNewName()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: new string('a', 101)));

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameAtMaxLength_HasNoValidationErrorForNewName()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: new string('a', 100)));

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionExceedsMaxLength_HasValidationErrorForNewDescription()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newDescription: new string('a', 1001)));

        result.ShouldHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionAtMaxLength_HasNoValidationErrorForNewDescription()
    {
        var validator = new UpdateOrganizationValidator();

        TestValidationResult<UpdateOrganization> result = await validator.TestValidateAsync(ValidCommand(newDescription: new string('a', 1000)));

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }
}
