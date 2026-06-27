using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Dtos;

/// <summary>
/// Pure static mapping functions from <see cref="User"/> domain objects to
/// <see cref="UserDto"/>.
/// </summary>
/// <remarks>
/// No I/O, no DI - pure functions. Cross-aggregate data (organization name)
/// is resolved by the query handler and passed in as a parameter, keeping
/// the mapper free of any data-access concern.
/// </remarks>
public static class UserMapper
{
    /// <summary>
    /// Maps a <see cref="User"/> to a <see cref="UserDto"/>.
    /// </summary>
    /// <param name="user">The user to map.</param>
    /// <param name="organizationName">
    /// The name of the user's organization, resolved by the handler via a
    /// scalar query rather than a navigation property.
    /// </param>
    public static UserDto ToDto(this User user, string organizationName)
        => new ()
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            OrgRole = user.OrgRole.ToString(),
            IsActive = user.IsActive,
            OrganizationId = user.OrganizationId,
            OrganizationName = organizationName,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
}
