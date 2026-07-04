namespace Ordinis.Infrastructure.FileStorage;

/// <summary>
/// Configuration options for <see cref="LocalFileStorageService"/>.
/// Bound from the <c>LocalStorage</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class LocalStorageOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "LocalStorage";

    /// <summary>
    /// Absolute or relative path to the directory where uploaded files are written.
    /// Defaults to <c>wwwroot/attachments</c> relative to the application root.
    /// </summary>
    public string BasePath { get; set; } = Path.Combine("wwwroot", "attachments");

    /// <summary>
    /// URL path prefix returned as part of the download URL.
    /// Must match the path served by ASP.NET Core's static file middleware.
    /// Defaults to <c>/attachments</c>.
    /// </summary>
    public string UrlPrefix { get; set; } = "/attachments";
}
