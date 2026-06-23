namespace Ordinis.Application.Common;

/// <summary>
/// Thrown by dispatcher when a command fails FluentValidation.
/// The global exception middleware maps to <c>422 Unprocessable Entity</c>
/// with a Problem Details body.
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// Validation errors keyed by field name.
    /// Value is the list of error messages for that field.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class with the specified validation errors.
    /// </summary>
    /// <param name="errors">Validation errors produced by FluentValidation.</param>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}
