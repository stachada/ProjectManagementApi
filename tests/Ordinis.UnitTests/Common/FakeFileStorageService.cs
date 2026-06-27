using Ordinis.Application.Common;

namespace Ordinis.UnitTests.Common;

/// <summary>
/// Records calls made by handlers under test instead of touching real storage.
/// </summary>
internal sealed class FakeFileStorageService : IFileStorageService
{
    public string? UploadedFileName { get; private set; }

    public string? DeletedDownloadUrl { get; private set; }

    public string DownloadUrlToReturn { get; set; } = "https://storage.test/files/default";

    public Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        UploadedFileName = fileName;
        return Task.FromResult(DownloadUrlToReturn);
    }

    public Task DeleteAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        DeletedDownloadUrl = downloadUrl;
        return Task.CompletedTask;
    }
}
