using Ordinis.Api.Common;

namespace Ordinis.Api.MinimalApis;

/// <summary>
/// Registers placeholder authentication Minimal API endpoints.
/// </summary>
/// <remarks>
/// These endpoints are scaffolded stubs only — full JWT + refresh token implementation lands in
/// Phase 8. Both currently return <c>501 Not Implemented</c>.
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps <c>POST /auth/login</c> and <c>POST /auth/refresh</c> as <c>501 Not Implemented</c>
    /// placeholders.
    /// </summary>
    /// <param name="routes">The endpoint route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/auth/login", (HttpContext context) => Results.Problem(ProblemDetailsFactory.Create(
            context,
            StatusCodes.Status501NotImplemented,
            "Not implemented",
            "Authentication is not yet implemented.")));

        routes.MapPost("/auth/refresh", (HttpContext context) => Results.Problem(ProblemDetailsFactory.Create(
            context,
            StatusCodes.Status501NotImplemented,
            "Not implemented",
            "Refresh is not yet implemented.")));

        return routes;
    }
}
