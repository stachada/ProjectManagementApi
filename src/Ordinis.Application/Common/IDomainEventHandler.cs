using Ordinis.Domain.Common;

namespace Ordinis.Application.Common;

/// <summary>
/// Handles a domain event published from the Outbox pattern.
/// Register implementations with DI; <c>OutboxDispatcherJob</c> resolves
/// registered handlers for each event type and invokes them in sequence.
/// </summary>
/// <typeparam name="TEvent">The concrete domain event type this handler processes.</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the domain event.
    /// </summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
