using FluentValidation.TestHelper;
using Ordinis.Application.Tasks.Commands;
using Ordinis.Domain.Projects;
using Ordinis.Domain.Tasks;
using Ordinis.Domain.Users;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Tasks.Validators;

/// <summary>
/// Verifies <see cref="CreateTaskValidator"/> rules, including the async board-existence
/// and assignee-existence checks run against the database.
/// </summary>
public sealed class CreateTaskValidatorTests
{
    private static CreateTask ValidCommand(Guid boardId, Guid? assigneeId = null)
        => new (
            boardId,
            "Fix login bug",
            "Description",
            Priority.Medium,
            assigneeId,
            null,
            Guid.CreateVersion7());

    [Fact]
    public async Task TestValidateAsync_EmptyBoardId_HasValidationErrorForBoardId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateTaskValidator(db);

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.BoardId);
    }

    [Fact]
    public async Task TestValidateAsync_BoardDoesNotExist_HasValidationErrorForBoardId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateTaskValidator(db);

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.BoardId)
            .WithErrorMessage("Board does not exist or has been archived.");
    }

    [Fact]
    public async Task TestValidateAsync_BoardArchived_HasValidationErrorForBoardId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        board.Archive();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(ValidCommand(board.Id));

        result.ShouldHaveValidationErrorFor(x => x.BoardId)
            .WithErrorMessage("Board does not exist or has been archived.");
    }

    [Fact]
    public async Task TestValidateAsync_BoardExistsAndNotArchived_HasNoValidationErrorForBoardId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(ValidCommand(board.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.BoardId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_TitleEmptyOrWhitespace_HasValidationErrorForTitle(string title)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id) with { Title = title };

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public async Task TestValidateAsync_TitleExceedsMaxLength_HasValidationErrorForTitle()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id) with { Title = new string('a', 201) };

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public async Task TestValidateAsync_TitleAtMaxLength_HasNoValidationErrorForTitle()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id) with { Title = new string('a', 200) };

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public async Task TestValidateAsync_PriorityNotAValidEnumValue_HasValidationErrorForPriority()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id) with { Priority = (Priority)99 };

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Priority);
    }

    [Fact]
    public async Task TestValidateAsync_AssigneeDoesNotExist_HasValidationErrorForAssigneeId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id, assigneeId: Guid.CreateVersion7());

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.AssigneeId)
            .WithErrorMessage("Assignee user does not exist.");
    }

    [Fact]
    public async Task TestValidateAsync_AssigneeExists_HasNoValidationErrorForAssigneeId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        User user = UserBuilder.Create();
        db.Boards.Add(board);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id, assigneeId: user.Id);

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Fact]
    public async Task TestValidateAsync_AssigneeNotProvided_HasNoValidationErrorForAssigneeId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id, assigneeId: null);

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.AssigneeId);
    }

    [Fact]
    public async Task TestValidateAsync_RequestedByUserIdEmpty_HasValidationErrorForRequestedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new CreateTaskValidator(db);
        CreateTask command = ValidCommand(board.Id) with { RequestedByUserId = Guid.Empty };

        TestValidationResult<CreateTask> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.RequestedByUserId);
    }
}
