namespace Ordinis.Api.Common;

/// <summary>
/// Marks a controller action as eligible for <see cref="IdempotencyMiddleware"/> handling —
/// an <c>Idempotency-Key</c> request header on this action's route caches and replays the
/// response for duplicate keys. Applied only to the create-style <c>POST</c> actions named in
/// the Phase 7 build plan; state-transition and other <c>POST</c> actions are deliberately left
/// unmarked.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;
