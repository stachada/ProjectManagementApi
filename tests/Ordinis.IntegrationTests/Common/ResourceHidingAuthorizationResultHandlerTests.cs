using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Api.Common;
using Ordinis.Application.Common;
using Ordinis.Infrastructure.Security.Authorization;
using Ordinis.IntegrationTests.Infrastructure;

namespace Ordinis.IntegrationTests.Common;

/// <summary>
/// Unit-style tests for <see cref="ResourceHidingAuthorizationResultHandler"/>. They live in the
/// integration tests project because it's the only one referencing <c>Ordinis.Api</c>, but they
/// boot no host - same approach as <see cref="RateLimitPartitionerTests"/>
/// </summary>
/// <param name="factory"></param>
public class ResourceHidingAuthorizationResultHandlerTests(OrdinisApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private readonly ResourceHidingAuthorizationResultHandler _handler = new();

    [Fact]
    public async Task HandleAsync_ForbiddenWithResourceNotVisibleReason_ThrowsNotFoundExceptionWithSameTypeAndId()
    {
        Guid resourceId = Guid.CreateVersion7();
        var reason = new ResourceNotVisibleReason(new PassThroughAuthorizationHandler(), "ProjectTask", resourceId);
        PolicyAuthorizationResult result = PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([reason]));
        (HttpContext context, RecordingAuthenticationService auth) = CreateContext();
        bool nextCalled = false;

        NotFoundException exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, Policy, result));

        // Same type, id and message as a handler-thrown NotFoundException, so GlobalExceptionMiddleware
        // renders a 404 body indistinguishable from a genuinly missing task.
        Assert.Equal("ProjectTask", exception.ResourceType);
        Assert.Equal<object>(resourceId, exception.ResourceId);
        Assert.Equal($"ProjectTask '{resourceId}' was not found.", exception.Message);
        Assert.False(nextCalled);
        Assert.Equal(0, auth.ForbidCount);
    }

    [Fact]
    public async Task HandleAsync_ForbiddenWithoutReason_DelegatesToDefaultHandler()
    {
        // e.g. a project Member hitting a ProjectAdmin action: visible, so a plain 403, not hidden.
        PolicyAuthorizationResult result = PolicyAuthorizationResult.Forbid(AuthorizationFailure.ExplicitFail());
        (HttpContext context, RecordingAuthenticationService auth) = CreateContext();
        bool nextCalled = false;

        await _handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, Policy, result);

        Assert.Equal(1, auth.ForbidCount); // default handler -> HttpContext.ForbidAsync -> JwtBearer OnForbidden (403)
        Assert.Equal(0, auth.ChallengeCount);
        Assert.False(nextCalled); // default handler doesn't call next, it ends the pipeline with 403
    }

    [Fact]
    public async Task HandleAsync_Challenged_DelegatesToDefaultHandler()
    {
        // Anonymous caller: must still get 401 from the authentication layer, never a 404.
        PolicyAuthorizationResult result = PolicyAuthorizationResult.Challenge();
        (HttpContext context, RecordingAuthenticationService auth) = CreateContext();
        bool nextCalled = false;

        await _handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, Policy, result);

        Assert.Equal(1, auth.ChallengeCount); // default handler -> HttpContext.ChallengeAsync -> JwtBearer OnChallenge (401)
        Assert.Equal(0, auth.ForbidCount);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task HandleAsync_Succeeded_CallsNext()
    {
        (HttpContext context, RecordingAuthenticationService auth) = CreateContext();
        bool nextCalled = false;

        await _handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, Policy, PolicyAuthorizationResult.Success());

        Assert.True(nextCalled);
        Assert.Equal(0, auth.ForbidCount);
        Assert.Equal(0, auth.ChallengeCount);
    }

    private static (HttpContext Context, RecordingAuthenticationService Auth) CreateContext()
    {
        var auth = new RecordingAuthenticationService();
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(auth)
            .BuildServiceProvider();

        return (new DefaultHttpContext { RequestServices = services }, auth);
    }

    /// <summary>
    /// Stands in for the real authentication service so the default handler's
    /// <c>ForbidAsync</c>/<c>ChallengeAsync</c> calls can be observed without JwtBearer.
    /// </summary>
    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public int ChallengeCount { get; private set; }

        public int ForbidCount { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            ChallengeCount++;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            ForbidCount++;
            return Task.CompletedTask;
        }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }
}


