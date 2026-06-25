namespace Ordinis.Application.Tasks.Dtos;

/// <summary>
/// Represents a file attachment as returned in <see cref="TaskDto"/>.
/// </summary>
public class AttachmentDto
{
    /// <summary>
    /// Unique identifier of the attachment.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Original file name as provided at upload time.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// MIME content type of the file (e.g. <c>application/pdf</c>.)
    /// </summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long SizeInBytes { get; init; }

    /// <summary>
    /// Pre-signed or premanent URL from which the file can be downloaded.
    /// </summary>
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this attachment was uploaded.
    /// </summary>
    public DateTimeOffset UploadedAt { get; init; }
}
