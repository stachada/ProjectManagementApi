namespace Ordinis.Application.Common;

/// <summary>
/// Thrown by a command handler when a concurrency update conflict is detected.
/// (i.e. the EF Core row version has changed since the command was loaded).
/// The global exception middleware maps this to <c>409 Conflict</c>
/// with a Problem Details body.
/// </summary>
public class ConcurrencyException : Exception
{
    /// <summary>
    /// The type name of the aggregate that experienced the conflict.
    /// Used to produce a descriptive error message.
    /// </summary>
    public string EntityType { get; }

    /// <summary>
    /// The ID of the aggregate that experienced the conflict.
    /// </summary>
    public Guid EntityId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyException"/> class with the specified entity type, entity ID, and inner exception.
    /// </summary>
    /// <param name="entityType">The type of the entity that caused the concurrency conflict.</param>
    /// <param name="entityId">The ID of the entity that caused the concurrency conflict.</param>
    /// <param name="inner">The original <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.</param>
    public ConcurrencyException(string entityType, Guid entityId, Exception inner)
        : base($"A concurrency conflict occurred on {entityType} '{entityId}'.", inner)
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyException"/> class for a
    /// proactive <c>If-Match</c> mismatch detected by <see cref="ConcurrencyGuard"/>, before
    /// any database write is attempted (i.e. there is no wrapped EF Core exception).
    /// </summary>
    /// <param name="entityType">The type of the entity that caused the concurrency conflict.</param>
    /// <param name="entityId">The ID of the entity that caused the concurrency conflict.</param>
    public ConcurrencyException(string entityType, Guid entityId)
        : base($"A concurrency conflict occurred on {entityType} '{entityId}'.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
