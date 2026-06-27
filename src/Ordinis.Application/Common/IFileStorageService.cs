namespace Ordinis.Application.Common;

/// <summary>
/// Contract for persisting and removing attachment files in blob storage.
/// </summary>
/// <remarks>
/// Implemented in <c>Ordinis.Infrastructure</c> (e.g. <c>LocalFileStorageService</c> for local disk,
/// or a future Azure Blob / S3 implementation). Command handlers in <c>Ordinis.Application</c> depend
/// on this abstraction only — they never reference a concrete storage provider.
/// </remarks>
public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file to storage and returns the URL it can be downloaded from.
    /// </summary>
    /// <param name="fileStream">The file content to upload.</param>
    /// <param name="fileName">Original filename, used to derive a unique storage key.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The URL or storage key the file was saved under.</returns>
    Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a previously uploaded file from storage.
    /// </summary>
    /// <param name="downloadUrl">The URL or storage key returned by <see cref="UploadAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string downloadUrl, CancellationToken cancellationToken = default);
}
