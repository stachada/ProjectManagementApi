namespace Ordinis.Application.Common;

/// <summary>
/// Converts a display name into a URL-friendly slug.
/// </summary>
/// <remarks>
/// Extracted as an interface so that both command handlers and validators
/// derive the slug via the same logic - eliminating the duplication that
/// existed when <c>CreateProjectHandler</c> and <c>CreateProjectValidator</c>
/// each run their own inline regex. An aggregate that auto-generates a slug
/// from a user-supplied name (currently <c>Project</c> and <c>Organization</c>)
/// injects this interface.
/// </remarks>
public interface ISlugGenerator
{
    /// <summary>
    /// Converts <paramref name="input"/> to a lowercase, hyphen-separated slug.
    /// </summary>
    /// <param name="input">The display name to slugify. Must not be null.</param>
    /// <returns>
    /// A lowercase alphanumeric string with hyphens replacing non-alphanumeric
    /// characters, with leading and trailing hyphens removed.
    /// Examples: "Acme Corp" -> "acme-corp", "Backend API v2" -> "backend-api-v2".
    /// </returns>
    string Slugify(string input);
}
