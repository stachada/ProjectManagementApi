using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Tasks;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="EditCommentValidator"/> rules, including the async comment-existence
/// and author-check run against the database.
/// </summary>
public class EditCommentValidatorTests
{
    private static EditComment ValidCommand(Guid taskId, Guid commentId)
        => new (
            TaskId: taskId,
            CommentId: commentId,
            NewContent: "This is the new content.",
            RequestedByUserId: Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Comment comment = CommentBuilder.Create();
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(comment.TaskId, comment.Id) with { RequestedByUserId = comment.AuthorId };

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyTaskId_HasValidationErrorForTaskId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.Empty,
                Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.TaskId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyCommentId_HasValidationErrorForCommentId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(
            ValidCommand(
                Guid.CreateVersion7(),
                Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.CommentId);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyNewContent_HasValidationErrorForNewContent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { NewContent = string.Empty };

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewContent);
    }

    [Fact]
    public async Task TestValidateAsync_NewContentAtMaxLength_HasNoValidationErrorForNewContent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { NewContent = new string('A', 10_000) };

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewContent);
    }

    [Fact]
    public async Task TestValidateAsync_NewContentTooLong_HasValidationErrorForNewContent()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { NewContent = new string('A', 10_001) };

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewContent);
    }

    [Fact]
    public async Task TestValidateAsync_EmptyRequestedByUserId_HasValidationErrorForRequestedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()) with { RequestedByUserId = Guid.Empty };

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }

    [Fact]
    public async Task TestValidateAsync_RequestedByUserIsNotAuthor_HasValidationErrorForCommand()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Comment comment = CommentBuilder.Create();
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(comment.TaskId, comment.Id) with { RequestedByUserId = Guid.CreateVersion7() };

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("Comment not found or user is not the author.");
    }

    [Fact]
    public async Task TestValidateAsync_CommentDoesNotExist_HasValidationErrorForCommand()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new EditCommentValidator(db);
        EditComment command = ValidCommand(Guid.CreateVersion7(), Guid.CreateVersion7());

        TestValidationResult<EditComment> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x)
            .WithErrorMessage("Comment not found or user is not the author.");
    }
}
