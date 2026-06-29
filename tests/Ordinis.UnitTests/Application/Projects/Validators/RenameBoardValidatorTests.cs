using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="RenameBoardValidator"/> rules, including the async
/// case-insensitive duplicate-name check scoped to the board's own project
/// (resolved via the board's <c>ProjectId</c> FK, excluding the board itself).
/// </summary>
public sealed class RenameBoardValidatorTests
{
    private static RenameBoard ValidCommand(Guid boardId, string newName = "Renamed Board")
        => new(boardId, newName);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(ValidCommand(board.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyBoardId_HasValidationErrorForBoardId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new RenameBoardValidator(db);

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.BoardId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NewNameEmptyOrWhitespace_HasValidationErrorForNewName(string newName)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(board.Id) with { NewName = newName };

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameExceedsMaxLength_HasValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(board.Id) with { NewName = new string('a', 101) };

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NewNameAtMaxLength_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(board.Id) with { NewName = new string('a', 100) };

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_BoardDoesNotExist_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new RenameBoardValidator(db);

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_DuplicateNameOnAnotherBoardInSameProject_HasValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Guid projectId = Guid.CreateVersion7();
        Board board = BoardBuilder.Create(projectId: projectId, name: "Sprint Board");
        Board otherBoard = BoardBuilder.Create(projectId: projectId, name: "Backlog");
        db.Boards.Add(board);
        db.Boards.Add(otherBoard);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(otherBoard.Id, newName: "Sprint Board");

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName)
            .WithErrorMessage("A board with this name already exists in the project.");
    }

    [Fact]
    public async Task TestValidateAsync_DuplicateNameDifferentCasingOnAnotherBoardInSameProject_HasValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Guid projectId = Guid.CreateVersion7();
        Board board = BoardBuilder.Create(projectId: projectId, name: "Sprint Board");
        Board otherBoard = BoardBuilder.Create(projectId: projectId, name: "Backlog");
        db.Boards.Add(board);
        db.Boards.Add(otherBoard);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(otherBoard.Id, newName: "sprint board");

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.NewName)
            .WithErrorMessage("A board with this name already exists in the project.");
    }

    [Fact]
    public async Task TestValidateAsync_RenamingToOwnCurrentName_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create(name: "Sprint Board");
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(board.Id, newName: "Sprint Board");

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_DuplicateNameInDifferentProject_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        Board otherProjectBoard = BoardBuilder.Create(name: "Sprint Board");
        db.Boards.Add(board);
        db.Boards.Add(otherProjectBoard);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(board.Id, newName: "Sprint Board");

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }

    [Fact]
    public async Task TestValidateAsync_NameUniqueWithinProject_HasNoValidationErrorForNewName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Board board = BoardBuilder.Create();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        var validator = new RenameBoardValidator(db);
        RenameBoard command = ValidCommand(board.Id, newName: "Brand New Name");

        TestValidationResult<RenameBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.NewName);
    }
}
