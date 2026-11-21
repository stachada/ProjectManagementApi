using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ordinis.Application.Common;
using Ordinis.Application.Users.Commands;

namespace Ordinis.Api.MinimalApis;

/// <summary>
/// Registers the authentication Minimal API endpoints - login, refresh, and logout are
/// focused, not-resource-CRUD concerns and belong here rather than in a controller.
/// </summary>
/// <remarks>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps <c>POST /auth/login</c> and <c>POST /auth/refresh</c> and <c>POST /auth/logout</c>.
    /// </summary>
    /// <param name="routes">The endpoint route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/auth/login",
            async (LoginRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var command = new Login(
                    OrganizationSlug: request.OrganizationSlug,
                    Email: request.Email,
                    Password: request.Password);

                LoginResult result = await dispatcher.SendAsync<Login, LoginResult>(command, ct);

                return Results.Ok(result);
            })
            .AllowAnonymous()
            .Produces<LoginResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        routes.MapPost("/auth/refresh",
            async (RefreshTokenRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var command = new RefreshToken(request.RefreshToken);
                LoginResult result = await dispatcher.SendAsync<RefreshToken, LoginResult>(command, ct);

                return Results.Ok(result);
            })
            .AllowAnonymous()
            .Produces<LoginResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        routes.MapPost("/auth/logout",
            async (HttpContext context, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = Guid.Parse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
                await dispatcher.SendAsync(new Logout(userId), ct);

                return Results.NoContent();
            })
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }
}

/// <summary>
/// Request body for <c>POST /auth/login</c>.
/// <param name="OrganizationSlug">The organization's URL-friendly slug.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The user's plaintext password.</param>
public sealed record LoginRequest(string OrganizationSlug, string Email, string Password);

/// <summary>
/// Request body for <c>POST /auth/refresh</c>.
/// </summary>
/// <param name="RefreshToken">The raw refresh token previously issued at login or the last refresh.</param>
public sealed record RefreshTokenRequest(string RefreshToken);
