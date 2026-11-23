using System.Text.RegularExpressions;

namespace Ordinis.AcceptanceTests.Support;

/// <summary>
/// The optional ending of a versioned write step, deciding what the request states in
/// <c>If-Match</c>: nothing appended → the resource's current version;
/// <c>" using the version &lt;persona&gt; read"</c> → the ETag that persona read earlier.
/// "… without stating a version" is not part of it: <c>RequestModifierSteps</c> handles that
/// phrase for every write step alike.
/// </summary>
/// <remarks>
/// The whole suffix is one capture group whose last alternative is empty, so it always
/// participates in the match: Reqnroll drops unmatched groups, which would otherwise shift the
/// step method's parameters.
/// </remarks>
internal static partial class VersionSuffix
{
    /// <summary>Append to a step regex (before <c>$</c>); binds to one <see cref="string"/> parameter.</summary>
    public const string Pattern = "( using the version \\w+ read|)";

    /// <summary>The <c>If-Match</c> value the suffix asks for.</summary>
    /// <param name="suffix">The captured suffix.</param>
    /// <param name="world">Holds the versions personas have read.</param>
    /// <param name="resourceId">The resource being written.</param>
    /// <param name="current">Reads the resource's current version.</param>
    public static async Task<string?> ResolveAsync(string suffix, World world, Guid resourceId, Func<Task<string>> current)
    {
        if (suffix.Length == 0)
        {
            return await current();
        }

        string reader = ReaderPattern().Match(suffix).Groups[1].Value;
        return world.ReadVersions.TryGetValue((reader, resourceId), out string? version)
            ? version
            : throw new InvalidOperationException($"{reader} has not read this resource in this scenario.");
    }

    [GeneratedRegex(@"version (\w+) read")]
    private static partial Regex ReaderPattern();
}
