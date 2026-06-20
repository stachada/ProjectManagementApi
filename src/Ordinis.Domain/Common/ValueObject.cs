namespace Ordinis.Domain.Common;

/// <summary>
/// Base class for value objects - domain concepts defined entirely by
/// their attributes, with no distinct identity.
/// </summary>
/// <remarks>
/// <para>
/// In DDD, a value object has three defining characteristics:
/// <list type="bullet">
///     <item>No identity - two instances with the same data are interchangeable.</item>
///     <item>Immutability - once created, the value cannot change. Modify by replacing, not mutating.</item>
///     <item>Self-validation - a value object is always in a valid state; invalid values cannot be constructed.</item>
/// </list>
/// </para>
/// <para>
/// Equality is structural: two <see cref="ValueObject"/> instances are equal
/// if and only if all their <see cref="GetEqualityComponents"/> are equal.
/// </para>
/// <para>
/// <b>When to use this base class vs. an enum:</b>
/// Use an <c>enum</c> for a finite, fixed set of named constants with no
/// additional behavior (e.g. <see cref="TaskStatus"/>, <see cref="Priority"/>).
/// Use <see cref="ValueObject"/> when the concept wraps a primitive and adds
/// validation or behavior - for example, an <c>EmailAddress</c> that enforces
/// format rules, or a <c>TaskTitle</c> that enforces length and non-emptiness.
/// </para>
/// <example>
/// <code>
/// public sealed class EmailAddress : ValueObject
/// {
///     public string Value { get; }
///
///     public EmailAddress(string value)
///     {
///        if (string.IsNullOrWhiteSpace(value))
///        {
///            throw new DomainException("Email address cannot be empty.", "email.empty");
///        }
///        if (!IsValidEmail(value))
///        {
///            throw new DomainException("Email address is not valid.", "email.invalid");
///        }
///
///        Value = value.Trim().ToLowerInvariant();
///     }
///
///     protected override IEnumerable<object> GetEqualityComponents()
///     {
///       yield return Value;
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public abstract class ValueObject
{
    /// <summary>
    /// Returns the components used for structural equality comparison.
    /// Each component is compared in order; all must be equal for two
    /// instances to be considered equal.
    /// </summary>
    /// <returns>
    /// An ordered sequence of components of the values that define this value object's identity.
    /// Yield each meaningful field in the same order in every implementation.
    /// </returns>
    protected abstract IEnumerable<object> GetEqualityComponents();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
        {
            return false;
        }

        var other = (ValueObject)obj;

        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => GetEqualityComponents()
            .Aggregate(0, (hash, component) => HashCode.Combine(hash, component));

    /// <summary>
    /// Structural equality operator.
    /// </summary>
    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null
            ? right is null
            : left.Equals(right);

    /// <summary>
    /// Structural inequality operator.
    /// </summary>
    public static bool operator !=(ValueObject? left, ValueObject? right)
        => !(left == right);
}
