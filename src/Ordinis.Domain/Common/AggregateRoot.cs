namespace Ordinis.Domain.Common;

/// <summary>
/// Base class for aggregate roots - the consistency boundary in DDD.
/// </summary>
/// <remarks>
/// <para>
/// An aggregate root is the only entry point for mutations to its aggregate.
/// External code never holds direct references to child entities inside
/// an aggregate and never mutates them directly.
/// </para>
/// <para>
/// <b>Domain events</b> are raised via <see cref="RaiseDomainEvent"/> during
/// a mutation method (e.g. <c>task.Move(...)</c>). They are collected in
/// <see cref="DomainEvents"/> and dispatched after <c>SaveChanges</c> succeeds,
/// via the Outbox pattern. The Infrastructure layer reads and clears this
/// collection inside <c>AppDbContext.SaveChanges</c>.
/// </para>
/// <para>
/// <b>Concurrency:</b> The <see cref="RowVersion"/> byte array is mapped by
/// EF Core as a concurrency token. EF Core automatically includes it in
/// <c>UPDATE</c> WHERE clauses and throws <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// if the token has changed since the entity was read. Command handlers
/// catch this exception and return <c>409 Conflict</c> with Problem Details.
/// </para>
/// </remarks>
public class AggregateRoot : AuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Domain events raised during this unit of work.
    /// Read by the Infrastructure layer after <c>SaveChanges</c>;
    /// cleared immediately after dispatch.
    /// Exposed as <see cref="IReadOnlyCollection{T}"/> to prevent
    /// external code from adding events directly.
    /// </summary>
    private IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// EF Core concurrency token. Automatically incremented by the database
    /// on every UPDATE. Mapped to a <c>rowversion</c> column (SQL Server)
    /// or <c>xmin</c> system column (PostgreSQL) in entity configuration.
    /// </summary>
    /// <remarks>
    /// This value is surfaced to API clients as an ETag header on GET responses
    /// and consumed on write operation via the <c>If-Match</c> header,
    /// providing end-to-end optimistic concurrency.
    /// </remarks>
    public byte[]? RowVersion { get; private set; }

    /// <summary>
    /// Initializes a new aggregate root with a generated Id.
    /// </summary>
    protected AggregateRoot() : base()
    { }

    /// <summary>
    /// Initializes an aggregate root with a specific Id.
    /// </summary>
    /// <param name="id"></param>
    protected AggregateRoot(Guid id) : base(id)
    { }

    /// <summary>
    /// Adds a domain event to the collection for dispatch after the
    /// current transaction commits.
    /// </summary>
    /// <param name="domainEvent">The event to raise. Must not be <c>null</c>.</param>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent, nameof(domainEvent));
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Clears all pending domain events.
    /// Called by the Infrastructure layer after events have been
    /// written to the Outbox table within the same transaction.
    /// </summary>
    /// <remarks>
    /// This is intentionally <c>internal</c> - only Infrastructure
    /// (same assembly via <c>InternalsVisibleTo</c>, or the same solution)
    /// should clear events. Domain code never calls this.
    /// </remarks>
    internal void ClearDomainEvents() => _domainEvents.Clear();
}
