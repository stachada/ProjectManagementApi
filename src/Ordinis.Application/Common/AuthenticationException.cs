namespace Ordinis.Application.Common;

/// <summary>
/// Thrown when login credentials are invalid for any reason (unknown organization, unknown
/// email, wrong password, or inactive account). Deliberately carries one generic message
/// across all of those cases - a specific reason would let a caller enumerate valid
/// organization slugs or registered emails.
/// </summary>
public sealed class AuthenticationException(string message = "Invalid organization, email, or password.") : Exception(message);
