using Ordinis.Domain.Organizations;

namespace Ordinis.Application.Organizations.Dtos;

/// <summary>
/// Static mapping methods from <see cref="Organization"/> domain objects
/// to <see cref="OrganizationDto"/>.
/// Pure function - no I/O, no DI dependencies.
/// </summary>
public static class OrganizationMapper
{
    /// <summary>
    /// Maps an <see cref="Organization"/> to an <see cref="OrganizationDto"/>.
    /// </summary>
    /// <param name="organization">The organization to map. Must not be null.</param>
    /// <param name="projectCount">
    /// Total number of projects belonging to this organization.
    /// Resolved by the query handler via a separate count query to avoid
    /// crossing aggregate boundaries via a navigation collection.
    /// </param>
    /// <returns></returns>
    public static OrganizationDto ToDto(this Organization organization, int projectCount)
        => new()
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            IsActive = organization.IsActive,
            CreatedAt = organization.CreatedAt,
            ProjectCount = projectCount
        };
}
