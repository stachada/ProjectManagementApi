using Ordinis.Domain.Users;

namespace Ordinis.Api.Users.Requests;

public sealed record CreateUserRequest(
    Guid OrganizationId,
    string DisplayName,
    string Email,
    string Password,
    Role OrgRole = Role.Member);
