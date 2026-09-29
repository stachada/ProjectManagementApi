using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="UnregisterWebhookValidator"/> rules.
/// </summary>
public sealed class UnregisterWebhookValidatorTests
{
    private static UnregisterWebhook ValidCommand()
        => new(Guid.CreateVersion7(), Guid.CreateVersion7(), [1, 2, 3, 4]);

    [Fact]
    public void TestValidate_ValidCommand_HasNoValidationErrors()
    {
        var validator = new UnregisterWebhookValidator();

        TestValidationResult<UnregisterWebhook> result = validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TestValidate_NullIfMatch_HasValidationErrorForIfMatch()
    {
        var validator = new UnregisterWebhookValidator();
        UnregisterWebhook command = ValidCommand() with { IfMatch = null };

        TestValidationResult<UnregisterWebhook> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
