using FluentValidation.TestHelper;
using Ordinis.Application.Organizations.Commands;

namespace Ordinis.UnitTests.Application.Organizations.Validators;

/// <summary>
/// Verifies <see cref="RenameOrganizationValidator"/> rules. All synchronous -
/// no database state is involved.
/// </summary>
public sealed class RenameOrganizationValidatorTests
{
    private static RenameOrganization ValidCommand(Guid? organizationId = null, string newName = "New Name")
        => new(organizationId ?? Guid.CreateVersion7(), newName);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new RenameOrganizationValidator();

        TestValidationResult<RenameOrganization> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyOrganizationId_HasValidationErrorForOrganizationId()
    {
        var validator = new RenameOrganizationValidator();

        TestValidationResult<RenameOrganization> result = await validator.TestValidateAsync(ValidCommand(organizationId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NewNameEmptyOrWhitespace_HasValidationErrorForNewName(string newName)
    {
        var validator = new RenameOrganizationValidator();

        TestValidationResult<RenameOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: newName));

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameExceedsMaxLength_HasValidationErrorForNewName()
    {
        var validator = new RenameOrganizationValidator();

        TestValidationResult<RenameOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: new string('a', 101)));

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameAtMaxLength_HasNoValidationErrorForNewName()
    {
        var validator = new RenameOrganizationValidator();

        TestValidationResult<RenameOrganization> result = await validator.TestValidateAsync(ValidCommand(newName: new string('a', 100)));

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }
}
