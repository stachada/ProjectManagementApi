using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ordinis.Application.Common;

namespace Ordinis.Infrastructure.FileStorage;

/// <summary>
/// Stores uploaded attachment files on the local file system under <c>wwwroot/attachments/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Files are served as static assets via ASP.NET Core's static file middleware — callers must
/// register <c>app.UseStaticFiles()</c> in <c>Program.cs</c> for download URLs to resolve.
/// </para>
/// <para>
/// <b>Swap note:</b> Replacing this with <c>AzureBlobStorageService</c> or
/// <c>S3FileStorageService</c> is a one-class change — the <see cref="IFileStorageService"/>
/// contract and all handler code remain unchanged.
/// </para>
/// </remarks>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly LocalStorageOptions _options;
    private readonly ILogger<LocalFileStorageService> _logger;

    /// <summary>
    /// Initializes the service with resolved configuration options and a logger.
    /// </summary>
    public LocalFileStorageService(
        IOptions<LocalStorageOptions> options,
        ILogger<LocalFileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The stored filename is <c>{uuidv7}_{sanitizedOriginalName}</c>, guaranteeing
    /// uniqueness and preventing path traversal while preserving the original name
    /// for readability. The returned URL is a relative path (e.g. <c>/attachments/xyz_file.png</c>)
    /// served by ASP.NET Core's static file middleware.
    /// </remarks>
    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var storedFileName = $"{Guid.CreateVersion7()}_{sanitizedName}";
        var directory = _options.BasePath;

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fullPath = Path.Combine(directory, storedFileName);

        await using var fileWriteStream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await fileStream.CopyToAsync(fileWriteStream, cancellationToken);

        return $"{_options.UrlPrefix.TrimEnd('/')}/{storedFileName}";
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Logs a warning if the file does not exist rather than throwing —
    /// a missing file on delete is recoverable (the DB row will still be removed),
    /// whereas throwing would leave an orphaned DB record pointing to a missing file.
    /// </remarks>
    public Task DeleteAsync(string downloadUrl, CancellationToken cancellationToken = default)
    {
        var prefix = _options.UrlPrefix.TrimEnd('/');

        if (!downloadUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "DeleteAsync: URL {DownloadUrl} does not start with expected prefix {Prefix}; skipping delete.",
                downloadUrl, prefix);

            return Task.CompletedTask;
        }

        var relativePath = downloadUrl[prefix.Length..].TrimStart('/');
        var fullPath = Path.Combine(_options.BasePath, relativePath);

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning(
                "DeleteAsync: file not found at {FilePath}; skipping delete.",
                fullPath);

            return Task.CompletedTask;
        }

        File.Delete(fullPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Strips path separators and normalises the filename so it is safe to use
    /// as part of a file system path. Spaces become underscores.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        // Path.GetFileName removes any leading directory component (e.g. "../../evil")
        var name = Path.GetFileName(fileName);

        // Replace characters that are invalid on common file systems or in URLs
        var invalid = Path.GetInvalidFileNameChars()
            .Concat([' ', '\t'])
            .ToHashSet();

        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        // Fallback for an empty result after sanitization
        return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized;
    }
}
