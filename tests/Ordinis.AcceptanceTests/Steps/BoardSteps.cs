using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Domain.Projects;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Boards: creation, viewing, renaming and archiving, plus read-backs. Versioned writes state
/// the board's current version unless the step ends with a <see cref="VersionSuffix"/>.
/// </summary>
[Binding]
internal sealed partial class BoardSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";
    private const string Version = VersionSuffix.Pattern;

    // ---- Arrangement ----

    [Given(@"^project ""([^""]+)"" also has a board ""([^""]+)""$")]
    public Task GivenTheProjectAlsoHasABoard(string project, string board) =>
        seeder.CreateBoardsAsync(world.ProjectNamed(project), board);

    [Given(@"^project ""([^""]+)"" has (\d+) boards in total$")]
    public Task GivenTheProjectHasNBoards(string project, int total)
    {
        Guid projectId = world.ProjectNamed(project);
        int existing = world.BoardProjects.Count(b => b.Value == projectId);
        string[] names = Enumerable.Range(existing + 1, total - existing)
            .Select(i => $"Generated board {i:000}")
            .ToArray();
        return seeder.CreateBoardsAsync(projectId, names);
    }

    [Given(@"^(\d+) tasks on board ""([^""]+)""$")]
    public async Task GivenNTasksOnBoard(int count, string board)
    {
        for (int i = 1; i <= count; i++)
        {
            await seeder.CreateTaskAsync(world.BoardNamed(board), $"Generated task {i:000}");
        }
    }

    [Given(@"^(\w+) has read the board ""([^""]+)""$")]
    public async Task GivenPersonaHasReadTheBoard(string persona, string board)
    {
        Guid boardId = world.BoardNamed(board);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Get, $"/api/v1/boards/{boardId}");
        Assert.True(response.IsSuccess && response.ETag is not null, $"Arrangement failed — {persona} could not read board '{board}': {response}");
        world.ReadVersions[(persona, boardId)] = response.ETag;
    }

    [Given(@"^(\w+) has since renamed the board ""([^""]+)"" to ""([^""]+)""$")]
    public async Task GivenPersonaHasRenamedTheBoard(string persona, string board, string newName)
    {
        await WhenPersonaRenamesTheBoard(persona, board, newName, suffix: string.Empty);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not rename board '{board}': {world.Response}");
    }

    // ---- Actions ----

    [When("^" + Actor + @" creates a board ""([^""]+)"" in project ""([^""]+)""$")]
    public Task WhenPersonaCreatesABoard(string persona, string board, string project) =>
        CreateBoardAsync(persona, board, project, new { name = board });

    [When("^" + Actor + @" creates a board (without a name|with a \d+-character name) in project ""([^""]+)""$")]
    public Task WhenPersonaCreatesABoardWithAnInvalidName(string persona, string variant, string project) =>
        CreateBoardAsync(persona, board: null, project,
            variant == "without a name" ? new { } : new { name = new string('b', Length(variant)) });

    [When("^" + Actor + @" views the board ""([^""]+)""$")]
    public Task WhenPersonaViewsTheBoard(string persona, string board) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/boards/{world.BoardNamed(board)}");

    [When("^" + Actor + @" views an unknown board$")]
    public Task WhenPersonaViewsAnUnknownBoard(string persona) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/boards/{Guid.CreateVersion7()}");

    [When("^" + Actor + @" renames the board ""([^""]+)"" to ""([^""]+)""" + Version + "$")]
    public async Task WhenPersonaRenamesTheBoard(string persona, string board, string newName, string suffix)
    {
        await WriteAsync(persona, HttpMethod.Put, board, "/name", new { newName }, suffix);

        if (world.Response.IsSuccess)
        {
            world.Boards[newName] = world.BoardNamed(board);
        }
    }

    [When("^" + Actor + @" renames the board ""([^""]+)"" (to a blank name|to a \d+-character name)$")]
    public Task WhenPersonaRenamesTheBoardInvalidly(string persona, string board, string variant) =>
        WriteAsync(persona, HttpMethod.Put, board, "/name",
            new { newName = variant == "to a blank name" ? "   " : new string('b', Length(variant)) }, string.Empty);

    [When("^" + Actor + @" archives the board ""([^""]+)""" + Version + "$")]
    public Task WhenPersonaArchivesTheBoard(string persona, string board, string suffix) =>
        WriteAsync(persona, HttpMethod.Post, board, "/archive", body: null, suffix);

    [When("^" + Actor + @" unarchives the board ""([^""]+)""" + Version + "$")]
    public Task WhenPersonaUnarchivesTheBoard(string persona, string board, string suffix) =>
        WriteAsync(persona, HttpMethod.Post, board, "/unarchive", body: null, suffix);

    // ---- Assertions ----

    [Then(@"^the response points to the new board$")]
    public void ThenTheResponsePointsToTheNewBoard()
    {
        Guid id = SuccessfulJson().GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/boards/{id}", world.Response.Location?.AbsolutePath);
    }

    [Then(@"^the board ""([^""]+)"" (is|is not) archived$")]
    public async Task ThenTheBoardIsArchived(string board, string verb)
    {
        Board stored = await seeder.FindBoardAsync(world.BoardNamed(board))
            ?? throw new InvalidOperationException($"Board '{board}' does not exist.");
        Assert.Equal(verb == "is", stored.IsArchived);
    }

    [Then(@"^the board details show name, project, archived flag, creation and update times$")]
    public void ThenTheBoardDetailsShowTheFields()
    {
        JsonElement board = SuccessfulJson();
        foreach (string field in new[] { "name", "projectId", "isArchived", "createdAt", "updatedAt" })
        {
            Assert.True(board.TryGetProperty(field, out _), $"Board details are missing '{field}'.");
        }
    }

    [Then(@"^the board details list the task ""([^""]+)""$")]
    public void ThenTheBoardDetailsListTheTask(string title) =>
        Assert.Contains(world.TaskNamed(title), EmbeddedTaskIds());

    [Then(@"^the board details list the (\d+) most recently created tasks$")]
    public void ThenTheBoardDetailsListTheMostRecentTasks(int count) =>
        Assert.Equal(
            world.TaskCreationOrder.TakeLast(count).Order(),
            EmbeddedTaskIds().Order());

    [Then(@"^the board details state the current version$")]
    public async Task ThenTheBoardDetailsStateTheCurrentVersion()
    {
        JsonElement board = SuccessfulJson();
        string current = await seeder.CurrentBoardVersionAsync(board.GetProperty("id").GetGuid());
        Assert.Equal(current, world.Response.ETag);
        Assert.Equal(current, $"\"{board.GetProperty("concurrencyToken").GetString()}\"");
    }

    // ---- Helpers ----

    private async Task CreateBoardAsync(string persona, string? board, string project, object body)
    {
        Guid projectId = world.ProjectNamed(project);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/projects/{projectId}/boards", body);

        if (response.IsSuccess && board is not null)
        {
            Guid boardId = response.Json.GetProperty("id").GetGuid();
            world.Boards[board] = boardId;
            world.BoardProjects[boardId] = projectId;
        }
    }

    private async Task WriteAsync(string persona, HttpMethod method, string board, string subPath, object? body, string suffix)
    {
        Guid boardId = world.BoardNamed(board);
        string? ifMatch = await VersionSuffix.ResolveAsync(suffix, world, boardId, () => seeder.CurrentBoardVersionAsync(boardId));
        await api.SendAsync(persona, method, $"/api/v1/boards/{boardId}{subPath}", body, ifMatch);
    }

    private List<Guid> EmbeddedTaskIds() =>
        SuccessfulJson().GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();

    private JsonElement SuccessfulJson()
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        return world.Response.Json;
    }

    private static int Length(string variant) =>
        int.Parse(DigitsPattern().Match(variant).Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsPattern();
}
