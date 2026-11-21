using Microsoft.AspNetCore.Authorization;

namespace Ordinis.Infrastructure.Security.Authorization;

/// <summary>
/// Authorization failure meaning "the caller can't see this resource at all" (it doesn't exist,
/// or they're not a member of its project). Reported to the client as 404, not 403, by
/// <c>ResourceHidingAuthorizationResultHandler</c>.
/// </summary>
public sealed class ResourceNotVisibleReason(IAuthorizationHandler handler, string resourceType, Guid resourceId)
    : AuthorizationFailureReason(handler, $"{resourceType} '{resourceId}' is not visible to the caller.")
{
    /// <summary>
    /// CLR type name passed to <c>NotFoundException</c> - must match what the endpoint's own handler would use.
    /// </summary>
    public string ResourceType { get; } = resourceType;

    public Guid ResourceId { get; } = resourceId;
}
