using System.Globalization;
using System.Text.Json;
using Ordinis.AcceptanceTests.Support;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Comments on tasks. "That comment" is the most recently added one (<see cref="World.LastComment"/>).
/// Read-backs go through a hidden org-Admin observer so they don't depend on a persona's access.
/// </summary>
[Binding]
internal sealed class CommentSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    // ---- Arrangement ----

    [Given(@"^(\w+) has commented ""([^""]+)"" on the task ""([^""]+)""$")]
    public async Task GivenPersonaHasCommented(string persona, string content, string title)
    {
        await WhenPersonaComments(persona, content, title);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not comment on '{title}': {world.Response}");
    }

    [Given(@"^(\w+) has removed that comment$")]
    public async Task GivenPersonaHasRemovedThatComment(string persona)
    {
        await WhenPersonaRemovesThatComment(persona);
        Assert.True(world.Response.IsSuccess, $"Arrangement failed — {persona} could not remove the comment: {world.Response}");
    }

    // ---- Actions ----

    [When("^" + Actor + @" comments ""([^""]+)"" on the task ""([^""]+)""$")]
    public Task WhenPersonaComments(string persona, string content, string title) =>
        AddCommentAsync(persona, content, title);

    [When("^" + Actor + @" comments (nothing|a blank text|a \d+-character text) on the task ""([^""]+)""$")]
    public Task WhenPersonaCommentsInvalidContent(string persona, string variant, string title) =>
        AddCommentAsync(persona, Content(variant), title);

    [When("^" + Actor + @" views the comments of ""([^""]+)""$")]
    public Task WhenPersonaViewsTheComments(string persona, string title) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{world.TaskNamed(title)}/comments");

    [When("^" + Actor + @" edits that comment to ""([^""]+)""$")]
    public Task WhenPersonaEditsThatComment(string persona, string newContent) =>
        EditAsync(persona, newContent);

    [When("^" + Actor + @" edits that comment to a (\d+)-character text$")]
    public Task WhenPersonaEditsThatCommentToLongText(string persona, int length) =>
        EditAsync(persona, new string('c', length));

    [When("^" + Actor + @" removes that comment$")]
    public Task WhenPersonaRemovesThatComment(string persona)
    {
        (Guid taskId, Guid commentId) = LastComment();
        return api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/tasks/{taskId}/comments/{commentId}");
    }

    [When("^" + Actor + @" removes an unknown comment from the task ""([^""]+)""$")]
    public Task WhenPersonaRemovesAnUnknownComment(string persona, string title) =>
        api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/tasks/{world.TaskNamed(title)}/comments/{Guid.CreateVersion7()}");

    [When("^" + Actor + @" removes the comment ""([^""]+)"" from the task ""([^""]+)""$")]
    public async Task WhenPersonaRemovesTheComment(string persona, string content, string title)
    {
        Guid taskId = world.TaskNamed(title);
        JsonElement comment = (await CommentsAsync(title)).Single(c => c.GetProperty("content").GetString() == content);
        await api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/tasks/{taskId}/comments/{comment.GetProperty("id").GetGuid()}");
    }

    // ---- Assertions ----

    [Then(@"^the comments of ""([^""]+)"" are:$")]
    public async Task ThenTheCommentsAre(string title, DataTable expected)
    {
        List<(string Author, string Content)> actual = (await CommentsAsync(title))
            .Select(c => (c.GetProperty("authorDisplayName").GetString()!, c.GetProperty("content").GetString()!))
            .ToList();
        List<(string, string)> wanted = expected.Rows.Select(r => (r["author"], r["content"])).ToList();
        Assert.Equal(wanted, actual);
    }

    [Then(@"^the comments of ""([^""]+)"" include (\d+) comments by (\w+) and (\w+)$")]
    public void ThenTheViewedCommentsInclude(string title, int count, string firstAuthor, string secondAuthor)
    {
        Assert.True(world.Response.IsSuccess, world.Response.ToString());
        List<string> authors = world.Response.Json.EnumerateArray().Select(c => c.GetProperty("authorDisplayName").GetString()!).ToList();
        Assert.Equal(count, authors.Count);
        Assert.Contains(firstAuthor, authors);
        Assert.Contains(secondAuthor, authors);
    }

    [Then(@"^the task ""([^""]+)"" has no comments$")]
    public async Task ThenTheTaskHasNoComments(string title) =>
        Assert.Empty(await CommentsAsync(title));

    [Then(@"^the task ""([^""]+)"" has (\d+) comments?$")]
    public async Task ThenTheTaskHasComments(string title, int count) =>
        Assert.Equal(count, (await CommentsAsync(title)).Count);

    [Then(@"^the response points to the comments of ""([^""]+)""$")]
    public void ThenTheResponsePointsToTheComments(string title) =>
        Assert.Equal($"/api/v1/tasks/{world.TaskNamed(title)}/comments", world.Response.Location?.AbsolutePath);

    // ---- Helpers ----

    private async Task AddCommentAsync(string persona, string? content, string title)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse response = await api.SendAsync(persona, HttpMethod.Post, $"/api/v1/tasks/{taskId}/comments", new { content });
        if (response.IsSuccess)
        {
            world.LastComment = (taskId, response.Json.GetProperty("id").GetGuid());
        }
    }

    private Task EditAsync(string persona, string newContent)
    {
        (Guid taskId, Guid commentId) = LastComment();
        return api.SendAsync(persona, HttpMethod.Put, $"/api/v1/tasks/{taskId}/comments/{commentId}", new { newContent });
    }

    private (Guid TaskId, Guid CommentId) LastComment() =>
        world.LastComment ?? throw new InvalidOperationException("No comment has been added in this scenario.");

    private async Task<List<JsonElement>> CommentsAsync(string title)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse response = await api.SendAsUserAsync(
            await ObserverForTaskAsync(taskId), HttpMethod.Get, $"/api/v1/tasks/{taskId}/comments");
        Assert.True(response.IsSuccess, response.ToString());
        return response.Json.EnumerateArray().ToList();
    }

    private async Task<Guid> ObserverForTaskAsync(Guid taskId)
    {
        Guid boardId = (await seeder.FindTaskAsync(taskId))?.BoardId
            ?? throw new InvalidOperationException("The task no longer exists.");
        return await seeder.ObserverForBoardAsync(boardId);
    }

    private static string? Content(string variant) => variant switch
    {
        "nothing" => null,
        "a blank text" => "   ",
        _ => new string('c', int.Parse(System.Text.RegularExpressions.Regex.Match(variant, @"\d+").Value, CultureInfo.InvariantCulture)),
    };
}
