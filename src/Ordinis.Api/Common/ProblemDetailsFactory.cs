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
    /// <param name="type">
    /// Optional machine-readable problem type URI. When omitted, defaults to a generic
    /// <c>https://httpstatuses.io/{statusCode}</c> URL. Pass a specific value (e.g. derived from
    /// <see cref="Ordinis.Domain.Common.DomainException.ErrorCode"/>) to let API clients distinguish
    /// error causes that share the same HTTP status code.
    /// </param>
    public static ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string title,
        string? detail = null,
        string? type = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = type ?? $"https://httpstatuses.io/{statusCode}",
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
        => CreateValidation(context, errors, StatusCodes.Status422UnprocessableEntity, "One or more validation errors occurred.");

    /// <summary>
    /// Creates a <see cref="ValidationProblemDetails"/> instance (<c>400 Bad Request</c>) from
    /// ASP.NET Core's automatic model-binding/model-state validation — malformed JSON, a route or
    /// query value that can't convert to its target type, a missing required field, etc. Kept
    /// distinct from <see cref="CreateValidation(HttpContext, IReadOnlyDictionary{string, string[]})"/>
    /// (FluentValidation's <c>422</c>): a request ASP.NET Core can't even bind is malformed
    /// (<c>400</c>), whereas a request that binds but violates a business rule is unprocessable
    /// (<c>422</c>).
    /// </summary>
    /// <param name="context">The current request's <see cref="HttpContext"/>.</param>
    /// <param name="errors">Validation errors keyed by field name.</param>
    public static ValidationProblemDetails CreateModelBindingValidation(
        HttpContext context,
        IReadOnlyDictionary<string, string[]> errors)
        => CreateValidation(context, errors, StatusCodes.Status400BadRequest, "One or more request fields were invalid.");

    private static ValidationProblemDetails CreateValidation(
        HttpContext context,
        IReadOnlyDictionary<string, string[]> errors,
        int statusCode,
        string title)
    {
        var problemDetails = new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
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
