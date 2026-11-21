using Ordinis.Application.Common;

namespace Ordinis.UnitTests.Common;

internal sealed class FakeCurrentUserService(Guid userId, Guid organizationId, bool isOrgAdmin = true) : ICurrentUserService
{
    public Guid UserId { get; } = userId;

    public Guid OrganizationId { get; } = organizationId;

    public bool IsOrgAdmin { get; } = isOrgAdmin;
}
