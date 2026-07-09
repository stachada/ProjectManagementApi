namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/users/{id}</c>.
/// </summary>
/// <param name="DisplayName">The new display name. Must not be empty.</param>
/// <param name="RequestedByUserId">The ID of the user performing the update.</param>
public sealed record UpdateUserRequest(
    string DisplayName,
    Guid RequestedByUserId);
