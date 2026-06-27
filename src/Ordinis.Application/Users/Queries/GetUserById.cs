using Microsoft.EntityFrameworkCore;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Dtos;
using Ordinis.Domain.Users;

namespace Ordinis.Application.Users.Queries;

// Command
/// <summary>
/// Returns the full detail view of a single user.
/// </summary>
/// <param name="UserId">The ID of the user to retrieve.</param>
public sealed record GetUserById(Guid UserId) : IQuery<UserDto>;

// Handler
/// <summary>
/// Handles <see cref="GetUserById"/> queries.
/// </summary>
/// <remarks>
/// Organization name is resolved via a separate scalar projection rather than
/// a navigation property — the <c>Organization</c> aggregate is independent
/// and must not be navigated as an object graph from within the User handler.
/// </remarks>
internal sealed class GetUserByIdHandler(IAppDbContext db)
    : IQueryHandler<GetUserById, UserDto>
{
    public async Task<UserDto> HandleAsync(GetUserById query, CancellationToken ct)
    {
        User user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == query.UserId, ct)
                ?? throw new NotFoundException(nameof(User), query.UserId);

        // Resolve organization name via a scalar projection — no navigation
        // property across the Organization aggregate boundary.
        var organizationName = await db.Organizations
            .Where(o => o.Id == user.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(ct)
                ?? string.Empty;

        return user.ToDto(organizationName);
    }
}
