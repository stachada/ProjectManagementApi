using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Security.Authorization;

namespace Ordinis.Api.Common;

/// <summary>
/// Turns a "caller can't see this resource" authorization failure into the same 404 a genuinely
/// missing resource produces, by throwing the real <see cref="NotFoundException"/> so
/// <see cref="GlobalExceptionMiddleware"/> renders an identical Problem Details body. Every other
/// outcome (401 challenge, plain 403, success) goes to the framework's default handler.
/// </summary>
public sealed class ResourceHidingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ResourceNotVisibleReason? notVisible = authorizeResult.AuthorizationFailure?.FailureReasons
            .OfType<ResourceNotVisibleReason>()
            .FirstOrDefault();

        if (authorizeResult.Forbidden && notVisible is not null)
        {
            throw new NotFoundException(notVisible.ResourceType, notVisible.ResourceId);
        }

        return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
