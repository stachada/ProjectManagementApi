namespace Ordinis.Api.Users.Requests;

/// <summary>
/// Request body for <c>PUT /api/v1/users/{id}</c>. The user performing the update is always the
/// authenticated caller, who must be the target user or an org Admin of the same organization.
/// </summary>
/// <param name="DisplayName">The new display name. Must not be empty.</param>
public sealed record UpdateUserRequest(string DisplayName);
