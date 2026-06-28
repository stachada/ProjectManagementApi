using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="AddAttachmentValidator"/> rules.
/// </summary>
public class AddAttachmentValidatorTests
{
    private static AddAttachment ValidCommand(Guid taskId)
        => new (
            TaskId: taskId,
            FileName: "document.pdf",
            ContentType: "application/pdf",
            SizeInBytes: 1024,
            FileStream: new MemoryStream(new byte[1024]),
            UploadedByUserId: Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        var validator = new AddAttachmentValidator();

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        var validator = new AddAttachmentValidator();

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyFileName_HasValidationErrorForFileName()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileName = string.Empty };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public async Task TestValidateAsync_FileNameTooLong_HasValidationErrorForFileName()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileName = new string('A', 256) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public async Task TestValidateAsync_FileNameAtMaxLength_HasNoValidationErrorForFileName()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileName = new string('A', 255) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyContentType_HasValidationErrorForContentType()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { ContentType = string.Empty };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public async Task TestValidateAsync_ContentTypeTooLong_HasValidationErrorForContentType()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { ContentType = new string('A', 101) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public async Task TestValidateAsync_ContentTypeAtMaxLength_HasNoValidationErrorForContentType()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { ContentType = new string('A', 100) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public async Task TestValidateAsync_ZeroSizeInBytes_HasValidationErrorForSizeInBytes()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { SizeInBytes = 0 };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.SizeInBytes)
            .WithErrorMessage("SizeInBytes must be greater than zero.");
    }

    [Fact]
    public async Task TestValidateAsync_NegativeSizeInBytes_HasValidationErrorForSizeInBytes()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { SizeInBytes = -1 };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.SizeInBytes)
            .WithErrorMessage("SizeInBytes must be greater than zero.");
    }

    [Fact]
    public async Task TestValidateAsync_PositiveSizeInBytes_HasNoValidationErrorForSizeInBytes()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { SizeInBytes = 1 };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.SizeInBytes);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyFileStream_HasValidationErrorForFileStream()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileStream = null! };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.FileStream);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUploadedByUserId_HasValidationErrorForUploadedByUserId()
    {
        var validator = new AddAttachmentValidator();
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { UploadedByUserId = Guid.Empty };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.UploadedByUserId);
    }
}
