namespace Ordinis.Domain.Common;

/// <summary>
/// Exception thrown when a domain invariant is violated.
/// </summary>
/// <remarks>
/// <para>
/// Throw this from aggregate root methods when a requested operation
/// violates a business rule - not for validation (that's FluentValidation
/// in the Application layer), but for invariants enforced by the domain
/// model itself.
/// </para>
/// <para>
/// Examples of correct usage:
/// <list type="bullet">
///    <item>Moving a task to a status that is not a legal transition from its current status.</item>
///    <item>Adding a comment to a task that has been deleted.</item>
///    <item>Assigning a task to a user who is not a member of the project.</item>
/// </list>
/// </para>
/// <para>
/// The global exception-handling middleware catches this exception
/// and maps it to an HTTP <c>422 Unprocessable Entity</c> response with a
/// Problem Details body (RFC 9457).
/// </para>
/// </remarks>
public sealed class DomainException : Exception
{
    /// <summary>
    /// A machine-readable error code for use in Problem Details responses.
    /// Follows the convention <c>domain.entity.rule-violated</c>.
    /// e.g. <c>"task.invalid-status-transition"</c>.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Initializes a new <see cref="DomainException"/> with a human-readable
    /// message and a machine-readable error code.
    /// </summary>
    /// <param name="message">
    /// Human-readable description of the violated invariant.
    /// This will appear in the Problem Details <c>detail</c> field.
    /// </param>
    /// <param name="errorCode">
    /// Machine-readable code, e.g. <c>"task.invalid-status-transition"</c>.
    /// This will appear in the Problem Details <c>type</c> field.
    /// </param>
    public DomainException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new <see cref="DomainException"/> wrapping an inner exception.
    /// </summary>
    /// <param name="message">
    /// Human-readable description.
    /// </param>
    /// <param name="errorCode">
    /// Machine-readable error code.
    /// </param>
    /// <param name="innerException">
    /// The originating exception, if any.
    /// </param>
    public DomainException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
