using System.Text.RegularExpressions;

namespace Ordinis.Application.Common;

/// <summary>
/// Default <see cref="ISlugGenerator"/> implementation using a single
/// compiled regex replace.
/// </summary>
/// <remarks>
/// Registered as a singleton in <c>AddAplicationServices</c> - the
/// implementation is stateless and the compiled regex is shared across
/// all requests.
/// </remarks>
internal sealed class SlugGenerator : ISlugGenerator
{
    /// <summary>
    /// Pre-compiled regex that matches one or more non-alphanumeric characters.
    /// Compiled once at startup and reused across all calls.
    /// </summary>
    private static readonly Regex NonAlphanumericRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <inheritdoc/>
    public string Slugify(string input)
    {
       ArgumentException.ThrowIfNullOrWhiteSpace(input, nameof(input));

        return NonAlphanumericRegex
            .Replace(input.Trim().ToLowerInvariant(), "-")
            .Trim('-');
    }
}
