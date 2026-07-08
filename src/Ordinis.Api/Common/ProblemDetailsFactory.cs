using Microsoft.AspNetCore.Mvc;

namespace Ordinis.Api.Common;

/// <summary>
/// Builds <see cref="ProblemDetails"/> responses (RFC 9457) with a consistent shape across
/// every error case, always stamping the current request's correlation ID as an extension.
/// </summary>
public static class ProblemDetailsFactory
{
    /// <summary>
    /// Creates a <see cref="ProblemDetails"/> instance for a single-error response.
    /// </summary>
    /// <param name="context">The current request's <see cref="HttpContext"/>.</param>
    /// <param name="statusCode">The HTTP status code to report.</param>
    /// <param name="title">Short, human-readable summary of the problem.</param>
    /// <param name="detail">Human-readable explanation specific to this occurrence.</param>
    public static ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string title,
        string? detail = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = context.Request.Path,
        };

        AddCorrelationId(problemDetails, context);
        return problemDetails;
    }

    /// <summary>
    /// Creates a <see cref="ValidationProblemDetails"/> instance (<c>422 Unprocessable Entity</c>)
    /// from FluentValidation's field-keyed error dictionary.
    /// </summary>
    /// <param name="context">The current request's <see cref="HttpContext"/>.</param>
    /// <param name="errors">Validation errors keyed by field name.</param>
    public static ValidationProblemDetails CreateValidation(
        HttpContext context,
        IReadOnlyDictionary<string, string[]> errors)
    {
        var problemDetails = new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "One or more validation errors occurred.",
            Type = $"https://httpstatuses.io/{StatusCodes.Status422UnprocessableEntity}",
            Instance = context.Request.Path,
        };

        AddCorrelationId(problemDetails, context);
        return problemDetails;
    }

    private static void AddCorrelationId(ProblemDetails problemDetails, HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out object? correlationId))
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }
    }
}
