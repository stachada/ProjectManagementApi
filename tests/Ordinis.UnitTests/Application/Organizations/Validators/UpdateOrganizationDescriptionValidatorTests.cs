using FluentValidation.TestHelper;
using Ordinis.Application.Organizations.Commands;

namespace Ordinis.UnitTests.Application.Organizations.Validators;

/// <summary>
/// Verifies <see cref="UpdateOrganizationDescriptionValidator"/> rules. All
/// synchronous - no database state is involved.
/// </summary>
public sealed class UpdateOrganizationDescriptionValidatorTests
{
    private static UpdateOrganizationDescription ValidCommand(Guid? organizationId = null, string? newDescription = "Updated description")
        => new(organizationId ?? Guid.CreateVersion7(), newDescription);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new UpdateOrganizationDescriptionValidator();

        TestValidationResult<UpdateOrganizationDescription> result = await validator.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyOrganizationId_HasValidationErrorForOrganizationId()
    {
        var validator = new UpdateOrganizationDescriptionValidator();

        TestValidationResult<UpdateOrganizationDescription> result = await validator.TestValidateAsync(ValidCommand(organizationId: Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.OrganizationId);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionExceedsMaxLength_HasValidationErrorForNewDescription()
    {
        var validator = new UpdateOrganizationDescriptionValidator();

        TestValidationResult<UpdateOrganizationDescription> result = await validator.TestValidateAsync(ValidCommand(newDescription: new string('a', 1001)));

        result.ShouldHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionAtMaxLength_HasNoValidationErrorForNewDescription()
    {
        var validator = new UpdateOrganizationDescriptionValidator();

        TestValidationResult<UpdateOrganizationDescription> result = await validator.TestValidateAsync(ValidCommand(newDescription: new string('a', 1000)));

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }

    [Fact]
    public async Task TestValidateAsync_NewDescriptionNull_HasNoValidationErrorForNewDescription()
    {
        var validator = new UpdateOrganizationDescriptionValidator();

        TestValidationResult<UpdateOrganizationDescription> result = await validator.TestValidateAsync(ValidCommand(newDescription: null));

        result.ShouldNotHaveValidationErrorFor(x => x.NewDescription);
    }
}
