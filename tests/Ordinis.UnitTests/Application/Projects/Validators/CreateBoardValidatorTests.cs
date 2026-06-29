using FluentValidation.TestHelper;
using Ordinis.Application.Projects.Commands;
using Ordinis.Domain.Projects;
using Ordinis.UnitTests.Common;
using Ordinis.UnitTests.Common.Builders;

namespace Ordinis.UnitTests.Application.Projects.Validators;

/// <summary>
/// Verifies <see cref="CreateBoardValidator"/> rules, including the async
/// project-exists/not-archived check and the case-insensitive duplicate-name
/// check scoped to the project.
/// </summary>
public sealed class CreateBoardValidatorTests
{
    private static CreateBoard ValidCommand(Guid projectId, string name = "New Board")
        => new(projectId, Guid.CreateVersion7(), name);

    [Fact]
    public async Task TestValidateAsync_ValidCommand_HasNoValidationErrors()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(ValidCommand(project.Id));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task TestValidateAsync_EmptyProjectId_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateBoardValidator(db);

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(ValidCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public async Task TestValidateAsync_ProjectDoesNotExist_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        var validator = new CreateBoardValidator(db);

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(ValidCommand(Guid.CreateVersion7()));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId)
            .WithErrorMessage("Project not found or is archived.");
    }

    [Fact]
    public async Task TestValidateAsync_ProjectArchived_HasValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        project.Archive();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(ValidCommand(project.Id));

        result.ShouldHaveValidationErrorFor(x => x.ProjectId)
            .WithErrorMessage("Project not found or is archived.");
    }

    [Fact]
    public async Task TestValidateAsync_ProjectExistsAndNotArchived_HasNoValidationErrorForProjectId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(ValidCommand(project.Id));

        result.ShouldNotHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public async Task TestValidateAsync_CreatedByUserIdEmpty_HasValidationErrorForCreatedByUserId()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id) with { CreatedByUserId = Guid.Empty };

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.CreatedByUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task TestValidateAsync_NameEmptyOrWhitespace_HasValidationErrorForName(string name)
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id) with { Name = name };

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameExceedsMaxLength_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id) with { Name = new string('a', 101) };

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameAtMaxLength_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id) with { Name = new string('a', 100) };

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_DuplicateNameInSameProject_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        Board existing = BoardBuilder.Create(projectId: project.Id, name: "Sprint Board");
        db.Projects.Add(project);
        db.Boards.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id, name: "Sprint Board");

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("A board with this name already exists in the project.");
    }

    [Fact]
    public async Task TestValidateAsync_DuplicateNameDifferentCasingInSameProject_HasValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        Board existing = BoardBuilder.Create(projectId: project.Id, name: "Sprint Board");
        db.Projects.Add(project);
        db.Boards.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id, name: "sprint board");

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("A board with this name already exists in the project.");
    }

    [Fact]
    public async Task TestValidateAsync_DuplicateNameInDifferentProject_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        Project otherProject = ProjectBuilder.Create(name: "Other Project", slug: "other-project");
        Board existing = BoardBuilder.Create(projectId: otherProject.Id, name: "Sprint Board");
        db.Projects.Add(project);
        db.Projects.Add(otherProject);
        db.Boards.Add(existing);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id, name: "Sprint Board");

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public async Task TestValidateAsync_NameUniqueWithinProject_HasNoValidationErrorForName()
    {
        using TestAppDbContext db = TestDbContextFactory.Create();
        Project project = ProjectBuilder.Create();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var validator = new CreateBoardValidator(db);
        CreateBoard command = ValidCommand(project.Id, name: "Brand New Board");

        TestValidationResult<CreateBoard> result = await validator.TestValidateAsync(command);

        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }
}
