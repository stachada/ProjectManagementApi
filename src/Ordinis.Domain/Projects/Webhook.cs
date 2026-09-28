using Ordinis.Domain.Common;

namespace Ordinis.Domain.Projects;

/// <summary>
/// A registered outbound notification target for a project — an external URL that
/// receives an HTTP POST whenever a subscribed domain event fires for a task in this project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not an aggregate root.</b> <see cref="Webhook"/> is owned by the <see cref="Project"/>
/// aggregate. Create and remove registrations through <see cref="Project.RegisterWebhook"/>
/// and <see cref="Project.UnregisterWebhook"/> - never instantiate or delete this entity
/// directly from a handler.
/// </para>
/// <para>
/// <b>Event filtering:</b> <see cref="EventTypes"/> lists the event names (e.g.
/// <c>"task.moved"</c>) this webhook wants delivered. An empty list means "all supported
/// event types" - the common case for a simple integration that wants everything.
/// </para>
/// </remarks>
public class Webhook : Entity
{
    /// <summary>
    /// The project this webhook belongs to.
    /// </summary>
    public Guid ProjectId { get; private set; }

    /// <summary>
    /// The absolute URL that receives the HTTP POST delivery.
    /// </summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// Event names this webhook subscribes to (see <see cref="Common.WebhookEventTypes"/>
    /// in <c>Ordinis.Application</c> for the supported values). Empty means all event types.
    /// </summary>
    public IReadOnlyList<string> EventTypes { get; private set; } = [];

    /// <summary>
    /// The user who registered this webhook.
    /// </summary>
    public Guid RegisteredByUserId { get; private set; }

    /// <summary>
    /// UTC timestamp of when the webhook was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    #region Navigation properties
    /// <summary>
    /// The project this webhook belongs to.
    /// </summary>
    public Project? Project { get; private set; }
    #endregion

    private Webhook() { }

    /// <summary>
    /// Creates a new webhook registration.
    /// Called exclusively from <see cref="Project.RegisterWebhook"/>.
    /// </summary>
    internal static Webhook Create(
        Guid projectId,
        string url,
        IReadOnlyList<string> eventTypes,
        Guid registeredByUserId,
        DateTimeOffset registeredAt)
        => new()
        {
            ProjectId = projectId,
            Url = url,
            EventTypes = eventTypes,
            RegisteredByUserId = registeredByUserId,
            RegisteredAt = registeredAt
        };
}
