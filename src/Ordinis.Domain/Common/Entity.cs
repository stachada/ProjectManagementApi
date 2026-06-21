namespace Ordinis.Domain.Common;

/// <summary>
/// Base class for all domain entities.
/// Provides a stable <see cref="Id"/> identity and value-based equality
/// based on that identity (two entity instances with the same Id are equal,
/// regardless of reference).
/// </summary>
/// <remarks>
/// <para>
/// Identity equality is the defining characteristic of an entity in DDD:
/// an entity is not equal to another because all its fields match,
/// but because it has the same identity over time.
/// </para>
/// <para>
/// The <see cref="Id"/> is set at construction time and never changes.
/// Use <see cref="Guid.CreateVersion7()"/> in constructors rather than relying
/// on the database to generate it, so the Id is available immediately
/// for domain event payloads before the first SaveChanges call.
/// </para>
/// </remarks>
public abstract class Entity
{
    /// <summary>
    /// The unique identifier for this entity.
    /// Assigned at construction; never reassigned.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Initializes a new entity with a new unique <see cref="Id"/>.
    /// </summary>
    protected Entity() => Id = Guid.CreateVersion7();

    /// <summary>
    /// Initializes an entity with a specific <see cref="Id"/>.
    /// Use this overload when reconstituting an entity from persistence
    /// or when an Id needs to be predetermined (e.g. in tests).
    /// </summary>
    /// <param name="id">The identity to assign. Must not be <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="id"/> is <see cref="Guid.Empty"/>.</exception>
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity Id cannot be an empty Guid.", nameof(id));
        }

        Id = id;
    }

    #region Equality - identity - based, not reference-based
    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is not Entity other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        return Id == other.Id;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// Structural equality operator delegating to <see cref="Equals(object?)"/>.
    /// </summary>
    public static bool operator ==(Entity? left, Entity? right)
        => left is null
            ? right is null
            : left.Equals(right);

    /// <summary>
    /// Structural inequality operator.
    /// </summary>
    public static bool operator !=(Entity? left, Entity? right)
        => !(left == right);
    #endregion
}
