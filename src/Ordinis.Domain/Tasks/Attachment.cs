using Ordinis.Domain.Common;
using Ordinis.Domain.Users;

namespace Ordinis.Domain.Tasks;

/// <summary>
/// Represents a file attached to a <see cref="ProjectTask"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Metadata only:</b> This entity stores file metadata — name, type, size,
/// and storage location. The binary file itself lives in blob storage
/// (S3, Azure Blob Storage, etc.). The Application layer uploads the file
/// to blob storage first, then calls <see cref="ProjectTask.AddAttachment"/>
/// with the resulting storage URL. On removal, the Application layer calls
/// <see cref="ProjectTask.RemoveAttachment"/> first to get the
/// <see cref="StorageUrl"/>, then deletes the file from blob storage.
/// </para>
/// <para>
/// <b>Immutability:</b> Attachments are append-only. Once created, no
/// properties change — there is no edit operation. If a file needs replacing,
/// the old attachment is removed and a new one added. This is why
/// <c>Attachment</c> derives from <see cref="Entity"/> rather than
/// <see cref="AuditableEntity"/> — soft delete and <c>UpdatedAt</c>
/// tracking add no value for an immutable record.
/// </para>
/// <para>
/// <b>Ownership:</b> Attachments are owned by the <see cref="ProjectTask"/>
/// aggregate. Never create or remove an <c>Attachment</c> directly from a
/// handler — always go through <see cref="ProjectTask.AddAttachment"/> and
/// <see cref="ProjectTask.RemoveAttachment"/>.
/// </para>
/// </remarks>
public sealed class Attachment : Entity
{
    #region Properties
    /// <summary>
    /// The original filename as uploaded by the user (e.g. "screenshot.png").
    /// Stored for display purposes only — not used as a storage key.
    /// </summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// MIME type of the file (e.g. "image/png", "application/pdf").
    /// Used by API consumers to render or handle the file appropriately.
    /// </summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>
    /// File size in bytes. Stored for display and quota-enforcement purposes.
    /// </summary>
    public long SizeInBytes { get; private set; }

    /// <summary>
    /// URL or storage key pointing to the file in blob storage.
    /// The Application layer resolves this to a pre-signed download URL
    /// before returning it in API responses — never expose the raw storage
    /// key directly to API consumers.
    /// </summary>
    public string StorageUrl { get; private set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the file was uploaded.
    /// </summary>
    public DateTimeOffset UploadedAt { get; private set; }
    #endregion

    #region Foreign Keys
    /// <summary>
    /// The task this attachment belongs to.
    /// </summary>
    public Guid TaskId { get; private set; }

    /// <summary>
    /// The user who uploaded this file.
    /// </summary>
    public Guid UploadedByUserId { get; private set; }
    #endregion

    #region Navigation properties
    /// <summary>
    /// The task this attachment belongs to.
    /// </summary>
    public ProjectTask? Task { get; private set; }

    /// <summary>
    /// The user who uploaded this file.
    /// </summary>
    public User? UploadedByUser { get; private set; }
    #endregion

    #region Constructor
    private Attachment() { }

    /// <summary>
    /// Creates a new attachment record.
    /// Called exclusively from <see cref="ProjectTask.AddAttachment"/> —
    /// never instantiate directly from a handler.
    /// </summary>
    /// <param name="taskId">The task this file is attached to. Must not be empty.</param>
    /// <param name="fileName">Original filename. Must not be empty.</param>
    /// <param name="contentType">MIME type. Must not be empty.</param>
    /// <param name="sizeInBytes">File size in bytes. Must be greater than zero.</param>
    /// <param name="storageUrl">Blob storage URL or key. Must not be empty.</param>
    /// <param name="uploadedByUserId">The uploading user. Must not be empty.</param>
    /// <param name="uploadedAt">UTC timestamp of when the file was uploaded.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if any string argument is empty or <paramref name="taskId"/>
    /// or <paramref name="uploadedByUserId"/> is <see cref="Guid.Empty"/>.
    /// </exception>
    /// <exception cref="DomainException">
    /// Thrown if <paramref name="sizeInBytes"/> is zero or negative.
    /// </exception>
    internal static Attachment Create(
        Guid taskId,
        string fileName,
        string contentType,
        long sizeInBytes,
        string storageUrl,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageUrl);

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("TaskId cannot be empty.", nameof(taskId));
        }

        if (uploadedByUserId == Guid.Empty)
        {
            throw new ArgumentException("UploadedByUserId cannot be empty.", nameof(uploadedByUserId));
        }

        if (sizeInBytes <= 0)
        {
            throw new DomainException(
                "Attachment size must be greater than zero.",
                "attachment.invalid-size");
        }

        return new Attachment
        {
            TaskId = taskId,
            FileName = fileName.Trim(),
            ContentType = contentType.Trim(),
            SizeInBytes = sizeInBytes,
            StorageUrl = storageUrl,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = uploadedAt
        };
    }
    #endregion
}
