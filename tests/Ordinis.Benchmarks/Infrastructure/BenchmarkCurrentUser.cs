using Ordinis.Application.Common;

namespace Ordinis.Benchmarks.Infrastructure;

/// <summary>
/// Fixed caller identity for benchmarks that run query handlers outside an HTTP request,
/// replacing the claims-backed <c>CurrentUserService</c> registered by
/// <c>AddInfrastructureServices</c>.
/// </summary>
/// <param name="UserId">The caller's user ID.</param>
/// <param name="OrganizationId">The caller's organization ID.</param>
/// <param name="IsOrgAdmin">Whether the caller is an org-level Admin.</param>
internal sealed record BenchmarkCurrentUser(Guid UserId, Guid OrganizationId, bool IsOrgAdmin) : ICurrentUserService;
