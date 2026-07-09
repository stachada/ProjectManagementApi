using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="AddAttachmentValidator"/> rules.
/// </summary>
public class AddAttachmentValidatorTests
{
    private static AddAttachment ValidCommand(Guid taskId, Guid? uploadedByUserId = null)
        => new(
            TaskId: taskId,
            FileName: "document.pdf",
            ContentType: "application/pdf",
            SizeInBytes: 1024,
            FileStream: new MemoryStream(new byte[1024]),
            UploadedByUserId: uploadedByUserId ?? Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddAttachmentValidator(db);

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(
            ValidCommand(Guid.CreateVersion7(), user.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(
            ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyFileName_HasValidationErrorForFileName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileName = string.Empty };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public async Task TestValidateAsync_FileNameTooLong_HasValidationErrorForFileName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileName = new string('A', 256) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public async Task TestValidateAsync_FileNameAtMaxLength_HasNoValidationErrorForFileName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileName = new string('A', 255) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.FileName);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyContentType_HasValidationErrorForContentType()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { ContentType = string.Empty };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public async Task TestValidateAsync_ContentTypeTooLong_HasValidationErrorForContentType()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { ContentType = new string('A', 101) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public async Task TestValidateAsync_ContentTypeAtMaxLength_HasNoValidationErrorForContentType()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { ContentType = new string('A', 100) };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public async Task TestValidateAsync_ZeroSizeInBytes_HasValidationErrorForSizeInBytes()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { SizeInBytes = 0 };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.SizeInBytes)
            .WithErrorMessage("SizeInBytes must be greater than zero.");
    }

    [Fact]
    public async Task TestValidateAsync_NegativeSizeInBytes_HasValidationErrorForSizeInBytes()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { SizeInBytes = -1 };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.SizeInBytes)
            .WithErrorMessage("SizeInBytes must be greater than zero.");
    }

    [Fact]
    public async Task TestValidateAsync_PositiveSizeInBytes_HasNoValidationErrorForSizeInBytes()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { SizeInBytes = 1 };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.SizeInBytes);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyFileStream_HasValidationErrorForFileStream()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { FileStream = null! };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.FileStream);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyUploadedByUserId_HasValidationErrorForUploadedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7()) with { UploadedByUserId = Guid.Empty };

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.UploadedByUserId);
    }

    [Fact]
    public async Task TestValidateAsync_UploadedByUserIdDoesNotExist_HasValidationErrorForUploadedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7(), Guid.CreateVersion7());

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.UploadedByUserId)
            .WithErrorMessage("User not found.");
    }

    [Fact]
    public async Task TestValidateAsync_UploadedByUserIdExists_HasNoValidationErrorForUploadedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        User user = UserBuilder.Create();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new AddAttachmentValidator(db);
        AddAttachment command = ValidCommand(Guid.CreateVersion7(), user.Id);

        TestValidationResult<AddAttachment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.UploadedByUserId);
    }
}
