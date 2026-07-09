namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/users/{id}/reactivate</c>.
/// </summary>
/// <param name="RequestedByUserId">The ID of the user performing the reactivation.</param>
public sealed record ReactivateUserRequest(Guid RequestedByUserId);
