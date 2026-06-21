namespace Ordinis.Domain.Common;

/// <summary>
/// Extends <see cref="Entity"/> with audit timestamps and soft-delete support.
/// </summary>
/// <remarks>
/// <para>
/// <b>Audit timestamps</b> (<see cref="CreatedAt"/>, <see cref="UpdatedAt"/>) are
/// set automatically by <c>AppDbContext.SaveChanges</c> in the infrastructure layer -
/// the domain entity never assigns them directly.
/// </para>
/// <para>
/// <b>Soft delete</b>: instead of issuing a <c>DELETE</c> statement, set
/// <see cref="IsDeleted"/> to <c>true</c> and record <see cref="DeletedAt"/>.
/// A global EF Core query filter (<c>HasQueryFilter</c>) ensures soft-deleted
/// rows are invisible to all normal queries without any caller needing to remember
/// to filter them out.
/// </para>
/// <para>
/// Not all entities are auditable. <c>ProjectMember</c> (a join table) and
/// <c>Attachment</c> (append-only) can derive directly from <see cref="Entity"/>
/// if the extra columns add no value. Derive from <see cref="AuditableEntity"/>
/// for entities where knowing <i>when</i> something changed and
/// <i>whether</i> it was deleted matters to the business.
/// </para>
/// </remarks>
public abstract class AuditableEntity : Entity
{
    /// <summary>
    /// UTC timestamp of when this entity was first persisted.
    /// </summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>
    /// UTC timestamp of the most recent update to this entity.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>
    /// Whether this entity has been soft-deleted.
    /// When <c>true</c>, the global EF Core query filter excludes this row
    /// from all standard queries.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// UTC timestamp of when this entity was soft-deleted.
    /// <c>null</c> if the entity is not deleted.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>
    /// Initializes a new auditable entity with a generated Id.
    /// </summary>
    protected AuditableEntity() : base()
    { }

    /// <summary>
    /// Initializes a new auditable entity with a specific Id.
    /// </summary>
    protected AuditableEntity(Guid id) : base(id)
    { }

    /// <summary>
    /// Marks this entity as soft-deleted.
    /// Calling this method more than once is a no-op.
    /// </summary>
    /// <remarks>
    /// Prefer calling domain-specific delete methods on the aggregate root
    /// (e.g. <c>task.Delete(deletedBy)</c> which calls this method internally
    /// and fires the appropriate domain event.
    /// </remarks>
    public void SoftDelete(DateTimeOffset deletedAt)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = deletedAt;
    }

    /// <summary>
    /// Restores a previously soft-deleted entity.
    /// </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
    }
}
