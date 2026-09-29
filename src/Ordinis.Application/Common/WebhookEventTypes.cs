namespace Ordinis.Application.Common;

/// <summary>
/// The event names webhooks can subscribe to, and that appear in the <c>"event"</c> field of
/// the delivered envelope (e.g. <c>{ "event": "task.moved", ... }</c>).
/// </summary>
/// <remarks>
/// Referenced by <c>RegisterWebhookValidator</c> (Application) to validate the requested
/// <c>EventTypes</c> list, and by <c>WebhookDomainEventHandler</c> (Infrastructure) to map a
/// fired domain event to its webhook event name.
/// </remarks>
public static class WebhookEventTypes
{
    /// <summary>Fired when a <see cref="Domain.Tasks.TaskCreated"/> domain event occurs.</summary>
    public const string TaskCreated = "task.created";

    /// <summary>Fired when a <see cref="Domain.Tasks.TaskMoved"/> domain event occurs.</summary>
    public const string TaskMoved = "task.moved";

    /// <summary>Fired when a <see cref="Domain.Tasks.TaskAssigned"/> domain event occurs.</summary>
    public const string TaskAssigned = "task.assigned";

    /// <summary>Fired when a <see cref="Domain.Tasks.CommentAdded"/> domain event occurs.</summary>
    public const string CommentAdded = "comment.added";

    /// <summary>All supported event names, used to validate a webhook registration's requested list.</summary>
    public static readonly IReadOnlyList<string> All = [TaskCreated, TaskMoved, TaskAssigned, CommentAdded];
}
