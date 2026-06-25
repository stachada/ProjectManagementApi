namespace Ordinis.Application.Common;

/// <summary>
/// Thrown by query handlers when a requested resource does not exist.
/// (or has been soft-deleted and is therefore invisible to the global query filter).
/// </summary>
/// <remarks>
/// The global exception middleware in <c>Ordinis.Api</c> catches this and maps it
/// to a <c>404 Not Found</c> Problem Details response. Handlers should never catch
/// it themselves - let it propagate.
/// </remarks>
public class NotFoundException : Exception
{
    /// <summary>
    /// The CLR type name of the resource that was not found.
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// The identifier that was used in the lookup.
    /// </summary>
    public object ResourceId { get; }

    /// <param name="resourceType">CLR type name of the missing resource (e.g. <c>nameof(ProjectTask)</c>).</param>
    /// <param name="resourceId">The ID that was looked up.</param>
    public NotFoundException(string resourceType, object resourceId)
        : base($"{resourceType} '{resourceId}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}
