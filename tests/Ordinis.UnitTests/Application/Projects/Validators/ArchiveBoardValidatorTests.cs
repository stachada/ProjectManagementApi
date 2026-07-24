using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="ArchiveBoardValidator"/> rules.
/// </summary>
public sealed class ArchiveBoardValidatorTests
{
    private static ArchiveBoard ValidCommand()
        => new(Guid.CreateVersion7(), [1, 2, 3, 4]);

    [Fact]
    public void TestValidate_ValidCommand_HasNoValidationErrors()
    {
        var validator = new ArchiveBoardValidator();

        TestValidationResult<ArchiveBoard> result = validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TestValidate_NullIfMatch_HasValidationErrorForIfMatch()
    {
        var validator = new ArchiveBoardValidator();
        ArchiveBoard command = ValidCommand() with { IfMatch = null };

        TestValidationResult<ArchiveBoard> result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.IfMatch)
            .WithErrorMessage("If-Match header is required.");
    }
}
