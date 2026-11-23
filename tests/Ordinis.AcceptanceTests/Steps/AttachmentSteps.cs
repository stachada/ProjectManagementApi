using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ordinis.AcceptanceTests.Support;
using Ordinis.Infrastructure.FileStorage;
using Reqnroll;

namespace Ordinis.AcceptanceTests.Steps;

/// <summary>
/// Attachments on tasks: real <c>multipart/form-data</c> uploads to the API's local file storage.
/// Uploaded attachments are remembered by (task, file name) so later steps can refer to them.
/// </summary>
[Binding]
internal sealed class AttachmentSteps(World world, Seeder seeder, ApiDriver api)
{
    private const string Actor = "(" + ApiDriver.Anonymous + @"|\w+)";

    // ---- Arrangement ----

    [Given(@"^(\w+) has uploaded ""([^""]+)"" to the task ""([^""]+)""$")]
    public async Task GivenPersonaHasUploaded(string persona, string fileName, string title)
    {
        ApiResponse response = await UploadAsync(persona, title, fileName, "text/plain", ContentOf(fileName));
        Assert.True(response.IsSuccess, $"Arrangement failed — {persona} could not upload '{fileName}': {response}");
    }

    // ---- Actions ----

    [When("^" + Actor + @" uploads ""([^""]+)"" to the task ""([^""]+)""$")]
    public Task WhenPersonaUploads(string persona, string fileName, string title) =>
        UploadAsync(persona, title, fileName, "text/plain", ContentOf(fileName));

    [When("^" + Actor + @" uploads ""([^""]+)"" \(([^,]+), (\d+) bytes\) to the task ""([^""]+)""$")]
    public Task WhenPersonaUploadsSized(string persona, string fileName, string contentType, int size, string title) =>
        UploadAsync(persona, title, fileName, contentType, new byte[size]);

    [When("^" + Actor + @" uploads an empty file ""([^""]+)"" to the task ""([^""]+)""$")]
    public Task WhenPersonaUploadsAnEmptyFile(string persona, string fileName, string title) =>
        UploadAsync(persona, title, fileName, "text/plain", []);

    [When("^" + Actor + @" uploads a file with a (\d+)-character name to the task ""([^""]+)""$")]
    public Task WhenPersonaUploadsLongName(string persona, int length, string title) =>
        UploadAsync(persona, title, new string('f', length - 4) + ".log", "text/plain", ContentOf("long-name"));

    [When("^" + Actor + @" uploads a file with a (\d+)-character content type to the task ""([^""]+)""$")]
    public Task WhenPersonaUploadsLongContentType(string persona, int length, string title) =>
        UploadAsync(persona, title, "crash.log", "text/" + new string('x', length - 5), ContentOf("long-type"));

    [When("^" + Actor + @" views the attachments of ""([^""]+)""$")]
    public Task WhenPersonaViewsTheAttachments(string persona, string title) =>
        api.SendAsync(persona, HttpMethod.Get, $"/api/v1/tasks/{world.TaskNamed(title)}/attachments");

    [When("^" + Actor + @" removes the attachment ""([^""]+)"" from the task ""([^""]+)""$")]
    public Task WhenPersonaRemovesTheAttachment(string persona, string fileName, string title)
    {
        Guid taskId = world.TaskNamed(title);
        Guid attachmentId = world.Attachments.TryGetValue((taskId, fileName), out Guid id)
            ? id
            : throw new InvalidOperationException($"No attachment '{fileName}' was uploaded to '{title}' in this scenario.");
        return api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/tasks/{taskId}/attachments/{attachmentId}");
    }

    [When("^" + Actor + @" removes an unknown attachment from the task ""([^""]+)""$")]
    public Task WhenPersonaRemovesAnUnknownAttachment(string persona, string title) =>
        api.SendAsync(persona, HttpMethod.Delete, $"/api/v1/tasks/{world.TaskNamed(title)}/attachments/{Guid.CreateVersion7()}");

    // ---- Assertions ----

    /// <summary>Columns: file name, content type, size, uploaded by.</summary>
    [Then(@"^the attachments of ""([^""]+)"" are:$")]
    public async Task ThenTheAttachmentsAre(string title, DataTable expected)
    {
        List<JsonElement> actual = await AttachmentsAsync(title);
        Assert.Equal(expected.RowCount, actual.Count);

        foreach ((DataTableRow row, JsonElement attachment) in expected.Rows.Zip(actual))
        {
            Assert.Equal(row["file name"], attachment.GetProperty("fileName").GetString());
            Assert.Equal(row["content type"], attachment.GetProperty("contentType").GetString());
            Assert.Equal(long.Parse(row["size"], CultureInfo.InvariantCulture), attachment.GetProperty("sizeInBytes").GetInt64());
            Assert.True(attachment.TryGetProperty("uploadedByUserId", out JsonElement uploader),
                $"The attachment representation does not say who uploaded it: {attachment}");
            Assert.Equal(world.PersonaNamed(row["uploaded by"]).UserId, uploader.GetGuid());
        }
    }

    [Then(@"^the task ""([^""]+)"" has (\d+) attachments?$")]
    public async Task ThenTheTaskHasAttachments(string title, int expected) =>
        Assert.Equal(expected, (await AttachmentsAsync(title)).Count);

    [Then(@"^the task ""([^""]+)"" has no attachments$")]
    public async Task ThenTheTaskHasNoAttachments(string title) =>
        Assert.Empty(await AttachmentsAsync(title));

    [Then(@"^the response points to the attachments of ""([^""]+)""$")]
    public void ThenTheResponsePointsToTheAttachments(string title) =>
        Assert.Equal($"/api/v1/tasks/{world.TaskNamed(title)}/attachments", world.Response.Location?.AbsolutePath);

    [Then(@"^the task ""([^""]+)"" is listed on board ""([^""]+)"" with (\d+) attachments?$")]
    public async Task ThenTheBoardEntryShowsAttachments(string title, string board, int count)
    {
        Guid boardId = world.BoardNamed(board);
        ApiResponse tasks = await api.SendAsUserAsync(await seeder.ObserverForBoardAsync(boardId), HttpMethod.Get,
            $"/api/v1/boards/{boardId}/tasks?pageSize=100");
        Assert.True(tasks.IsSuccess, tasks.ToString());
        JsonElement entry = tasks.Json.EnumerateArray().Single(e => e.GetProperty("title").GetString() == title);
        Assert.Equal(count, entry.GetProperty("attachmentCount").GetInt32());
    }

    /// <summary>The file the upload wrote to the API's local storage has been deleted.</summary>
    [Then(@"^the stored file ""([^""]+)"" no longer exists$")]
    public void ThenTheStoredFileNoLongerExists(string fileName)
    {
        string url = world.AttachmentUrls
            .Single(kv => kv.Key.FileName == fileName).Value;
        LocalStorageOptions options = TestHost.Factory.Services.GetRequiredService<IOptions<LocalStorageOptions>>().Value;
        string relative = url[options.UrlPrefix.TrimEnd('/').Length..].TrimStart('/');
        Assert.False(File.Exists(Path.Combine(options.BasePath, relative)), $"The stored file for '{fileName}' still exists.");
    }

    // ---- Helpers ----

    private async Task<ApiResponse> UploadAsync(string persona, string title, string fileName, string contentType, byte[] content)
    {
        Guid taskId = world.TaskNamed(title);
        ApiResponse response = await api.UploadAsync(persona, $"/api/v1/tasks/{taskId}/attachments", fileName, contentType, content);
        if (response.IsSuccess)
        {
            world.Attachments[(taskId, fileName)] = response.Json.GetProperty("id").GetGuid();
            world.AttachmentUrls[(taskId, fileName)] = response.Json.GetProperty("downloadUrl").GetString()!;
        }

        return response;
    }

    private async Task<List<JsonElement>> AttachmentsAsync(string title)
    {
        Guid taskId = world.TaskNamed(title);
        Guid boardId = (await seeder.FindTaskAsync(taskId))?.BoardId
            ?? throw new InvalidOperationException($"Task '{title}' no longer exists.");
        ApiResponse response = await api.SendAsUserAsync(await seeder.ObserverForBoardAsync(boardId), HttpMethod.Get,
            $"/api/v1/tasks/{taskId}/attachments");
        Assert.True(response.IsSuccess, response.ToString());
        return response.Json.EnumerateArray().ToList();
    }

    private static byte[] ContentOf(string fileName) => Encoding.UTF8.GetBytes($"contents of {fileName}");
}
