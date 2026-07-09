namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/users/{id}/deactivate</c>.
/// </summary>
/// <param name="RequestedByUserId">The ID of the user performing the deactivation.</param>
public sealed record DeactivateUserRequest(Guid RequestedByUserId);
