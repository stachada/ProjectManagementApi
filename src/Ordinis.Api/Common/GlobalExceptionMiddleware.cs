using Microsoft.AspNetCore.Mvc;
using Ordinis.Application.Common;
using Ordinis.Domain.Common;

namespace Ordinis.Api.Common;

/// <summary>
/// Catches exceptions thrown by command/query handlers (via the dispatcher) or by controller
/// actions and translates them into RFC 9457 Problem Details responses:
/// <see cref="ValidationException"/> → <c>422</c>, <see cref="NotFoundException"/> → <c>404</c>,
/// <see cref="ConcurrencyException"/> → <c>409</c>, <see cref="DomainException"/> → <c>422</c>,
/// <see cref="BadHttpRequestException"/> (Minimal API route/query parameter binding failure,
/// e.g. <c>?page=abc</c>) → its own <see cref="BadHttpRequestException.StatusCode"/> (typically
/// <c>400</c>), anything else → <c>500</c>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="GlobalExceptionMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="logger">Logger used to record the exception before it is translated.</param>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger = logger;

    /// <summary>
    /// Runs the rest of the pipeline, catching and translating known exception types into
    /// Problem Details responses.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, ProblemDetailsFactory.CreateValidation(context, ex.Errors));
        }
        catch (NotFoundException ex)
        {
            _logger.LogInformation(ex, "Resource not found for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, ProblemDetailsFactory.Create(
                context, StatusCodes.Status404NotFound, "Resource not found", ex.Message));
        }
        catch (ConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, ProblemDetailsFactory.Create(
                context, StatusCodes.Status409Conflict, "Concurrency conflict", ex.Message));
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain rule violated for {Method} {Path}: {ErrorCode}",
                context.Request.Method, context.Request.Path, ex.ErrorCode);
            await WriteAsync(context, ProblemDetailsFactory.Create(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "Business rule violated",
                ex.Message,
                type: $"urn:ordinis:error:{ex.ErrorCode}"));
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning(ex, "Bad request for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, ProblemDetailsFactory.Create(
                context, ex.StatusCode, "Bad request", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, ProblemDetailsFactory.Create(
                context,
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "An unexpected error occurred while processing your request."));
        }
    }

    private static async Task WriteAsync(HttpContext context, ProblemDetails problemDetails)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        // Serialize by the runtime type, not the ProblemDetails-typed parameter — WriteAsJsonAsync's
        // generic overload would otherwise infer TValue as ProblemDetails and silently slice off
        // derived-only members, e.g. ValidationProblemDetails.Errors.
        await context.Response.WriteAsJsonAsync(problemDetails, problemDetails.GetType());
    }
}
