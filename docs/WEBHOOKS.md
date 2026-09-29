# Webhooks

This project lets a client register a URL on a project that receives an HTTP `POST` the
moment a task in that project fires one of four events: `task.created`, `task.moved`,
`task.assigned`, `comment.added`.

This doc explains the full round trip end to end — registration, the enqueue/deliver
split, retry/backoff, and how to try it yourself. For the domain event / Outbox mechanism
this builds on, see [REST_API_CONCEPTS.md#webhooks](REST_API_CONCEPTS.md#webhooks-) for the
concept writeup and the Outbox pattern it reuses.

---

## Why this exists

Polling an API for changes ("did anything happen since I last checked?") wastes requests
when nothing changed and adds latency when something did. Webhooks invert the
relationship — the server proactively pushes an event payload to a URL the client
registered in advance, so an integration (a Slack notifier, a CI trigger, another internal
service) reacts in near real-time without polling at all.

---

## Two components, not one

The plan going in called for a single `WebhookDispatcherService` subscribing directly to
`OutboxMessage` events. That turned out to be the wrong shape once `OutboxDispatcherJob`'s
actual transaction handling was accounted for:

`OutboxDispatcherJob` (`src/Ordinis.Infrastructure/Persistence/OutboxDispatcherJob.cs`)
holds an explicit DB transaction open for its *entire* fetch → dispatch → save cycle, so
the row locks it takes when claiming a batch are held until that batch's changes commit —
this is what makes multiple instances of the job safe to run concurrently (see its own
remarks for the full rationale). Any `IDomainEventHandler<T>` it invokes runs *inside* that
transaction. If a webhook handler tried to do the actual HTTP delivery — including a 3-attempt
retry with exponential backoff, which can legitimately take over a minute — right there, a
single slow or dead webhook URL would hold that transaction's row locks open for the
entire retry window, stalling every other domain event waiting in the same batch.

So the work is split across two independent pieces:

1. **`WebhookDomainEventHandler`** (`src/Ordinis.Infrastructure/Webhooks/WebhookDomainEventHandler.cs`)
   — a normal `IDomainEventHandler<T>` implementation, run by `OutboxDispatcherJob` inside
   its transaction like any other handler. It does no I/O beyond the same `AppDbContext` —
   it resolves which project the event belongs to, finds matching registered webhooks, and
   inserts one cheap `WebhookDelivery` row per match. Fast, no network calls, safe to run
   under the lock.
2. **`WebhookDeliveryDispatcherService`** (`src/Ordinis.Infrastructure/Webhooks/WebhookDeliveryDispatcherService.cs`)
   — a second, independent `BackgroundService`, structurally a near-twin of
   `OutboxDispatcherJob` itself (same `PeriodicTimer` polling loop, same provider-aware
   `UPDLOCK, READPAST` / `FOR UPDATE SKIP LOCKED` row-locking fetch, same
   fetch-inside-a-transaction shape for multi-instance safety). It polls
   `WebhookDeliveries` for due `Pending` rows and performs the actual HTTP `POST` — with
   retry and backoff — entirely *outside* any DB transaction, so a slow endpoint only ever
   blocks its own poll tick, never `OutboxDispatcherJob`'s.

This also means delivery attempts persist through a process restart mid-retry, and every
delivery attempt is queryable (`WebhookDeliveries.Status`/`AttemptCount`/`LastError`) —
a real audit trail that a literal fire-and-forget `Task.Run` wouldn't have given for free.

---

## The `Webhook` entity

`Webhook` (`src/Ordinis.Domain/Projects/Webhook.cs`) is owned by `Project`, the same
pattern as `ProjectMember` — created and removed only through
`Project.RegisterWebhook`/`UnregisterWebhook`, never instantiated directly. A project may
hold at most `Project.MaxWebhooks` (20) webhooks; `RegisterWebhook` throws a
`DomainException` (`project.webhook-limit-reached`) beyond that, the same invariant-guard
pattern as the last-Admin protection on `RemoveMember`.

`EventTypes` is the subscription filter — the event names (see below) a given webhook
wants delivered. An empty list means "everything"; a non-empty list is checked by
`WebhookDomainEventHandler` before a `WebhookDelivery` row is even created, so a webhook
scoped to `["task.moved"]` never sees a `comment.added` row in its delivery history at all.

---

## Step by step

### 1. Register a webhook

```http
POST /api/v1/projects/{id}/webhooks
If-Match: "AAAAAAAAB9E="
Content-Type: application/json

{ "url": "https://example.com/hook", "eventTypes": [], "registeredByUserId": "..." }
```

Handled by `WebhookEndpoints.cs` (Minimal API, not a controller — per this project's
"Minimal APIs for focused, non-resource routes" convention, alongside auth and search),
which dispatches `RegisterWebhook`. Like every other `Project`-mutating endpoint, it
requires `If-Match` (see [CONCURRENCY.md](CONCURRENCY.md)) and is idempotency-key aware
(`.WithMetadata(new IdempotentAttribute())` — a lambda route handler can't carry the
`[Idempotent]` C# attribute the way a controller action can, but `IdempotencyMiddleware`
reads endpoint metadata either way, so the effect is identical; see
[IDEMPOTENCY.md](IDEMPOTENCY.md)). `RegisterWebhookValidator` checks the project exists,
the URL is a well-formed absolute `http`/`https` URI, and every requested event type is
one `WebhookEventTypes` actually knows about.

### 2. A domain event fires and gets enqueued

Creating, moving, or assigning a task (or adding a comment) raises a domain event exactly
as it always has — this part of the pipeline is unchanged. `AppDbContext.SaveChangesAsync`
writes it to `OutboxMessages` in the same transaction as the aggregate change, and
`OutboxDispatcherJob` picks it up on its next poll tick (every 5 seconds).

For each `TaskCreated`/`TaskMoved`/`TaskAssigned`/`CommentAdded` message,
`WebhookDomainEventHandler`:

1. Resolves the owning project via `Task → Board → Project` (domain events carry a
   `TaskId`, not a `ProjectId`, so this hop is always needed).
2. Loads that project's registered webhooks, filtering by `EventTypes` as described above.
3. Builds the envelope once — `{ "event": "task.moved", "occurredAt": "...", "data": {...} }`
   — and inserts one `WebhookDelivery` row per matching webhook, `Status = Pending`,
   `NextAttemptAt = now`.

The `data` field is the domain event's own fields, serialized via its concrete runtime
type (`JsonSerializer.SerializeToElement(data, data.GetType())`) — the same
"serialize by concrete type, not by static `object`" approach `OutboxMessage.From` already
uses, so the envelope isn't relying on `System.Text.Json`'s more surprising
object-property polymorphic-serialization behavior.

### 3. `WebhookDeliveryDispatcherService` delivers it

On its own 5-second poll tick, it fetches due `Pending` rows and, for each, `POST`s
`Payload` to `Url` via a named `HttpClient("WebhookDelivery")` (10s timeout):

- **2xx response** → `Status = Delivered`, `DeliveredAt` set.
- **Anything else (non-2xx, timeout, connection refused, ...)** → `AttemptCount++`, and:
  - If `AttemptCount < MaxAttempts` (3): `NextAttemptAt = now + InitialBackoff *
    BackoffMultiplier^(AttemptCount - 1)` — 30s after the first failure, 60s after the
    second — and the row stays `Pending` for the next poll tick to retry.
  - Otherwise: `Status = Failed`, and an `ILogger` error is written with the delivery ID,
    URL, attempt count, and last error message.

A subtlety worth calling out: `HttpClient`'s own per-request timeout throws
`OperationCanceledException` even though nothing about the host is shutting down. The
delivery loop distinguishes "this request timed out" (a retryable failure) from "the host's
own `stoppingToken` was cancelled" (let it propagate, don't record a bogus failure) by
checking `cancellationToken.IsCancellationRequested` directly rather than the exception
type — matching type alone would have swallowed real timeouts as if they were shutdowns.

### 4. Unregister a webhook

```http
DELETE /api/v1/projects/{id}/webhooks/{webhookId}
If-Match: "AAAAAAAAB9F="
```

Dispatches `UnregisterWebhook`, same `If-Match` requirement, `204 No Content` on success.
Deliveries already enqueued for a since-unregistered webhook are **not** cancelled or
deleted — `WebhookDelivery` snapshots the target `Url` at enqueue time specifically so an
in-flight delivery (or its eventual `Failed` outcome) survives the registration being
removed, preserving the audit trail rather than silently discarding it.

---

## Event names

Defined in `WebhookEventTypes` (`src/Ordinis.Application/Common/WebhookEventTypes.cs`) —
the single source of truth for both `RegisterWebhookValidator`'s allow-list and
`WebhookDomainEventHandler`'s event-name mapping:

| Domain event | Webhook event name |
| --- | --- |
| `TaskCreated` | `task.created` |
| `TaskMoved` | `task.moved` |
| `TaskAssigned` | `task.assigned` |
| `CommentAdded` | `comment.added` |

---

## SSRF protection

**What SSRF is:** Server-Side Request Forgery is an attack where a client tricks a server
into making an HTTP request *on the attacker's behalf* to a destination the attacker
couldn't reach directly, but the server can. A webhook registration endpoint is a natural
target for this — "give me a URL, I'll `POST` to it later" has no inherent limit on what
that URL points at unless the server enforces one. Without a check, someone could register:

- `http://localhost:5432` — probing whatever's listening on the server's own loopback
  interface, including services never meant to be internet-facing.
- `http://169.254.169.254/latest/meta-data/iam/security-credentials/` — the cloud
  provider metadata endpoint present on every EC2/Azure/GCP VM by default, which often
  hands back the instance's own cloud credentials with no auth required (it's normally
  only reachable from inside the VM itself — exactly the reach a server-side request has
  and an outside attacker doesn't).
- `http://10.0.0.5:9200` — an internal service on a private network the API server can
  reach but the public internet can't.

The attacker never talks to any of these directly; the *server* makes the request for
them, using network access the attacker doesn't have. `WebhookDeliveryDispatcherService`
is exactly that kind of server-side-request-on-someone-else's-behalf machinery, so it's
the piece that needs the guard — not just the registration endpoint, since a URL that
resolves to something public at registration time can resolve somewhere private by the
time delivery actually runs (DNS rebinding, covered below).

`IWebhookUrlGuard` (`src/Ordinis.Application/Common/IWebhookUrlGuard.cs`, implemented by
`WebhookUrlGuard` in `src/Ordinis.Infrastructure/Webhooks/`) resolves a URL's host and
rejects it unless **every** resolved address is public and routable. It's checked in two
places, deliberately not just one:

- **`RegisterWebhookValidator`** — at registration time, so a client attempting to
  register an obviously-internal URL gets an immediate, clear `422` instead of silently
  succeeding.
- **`WebhookDeliveryDispatcherService`**, immediately before every single delivery
  attempt (not just the first) — this is the check that actually matters. Registration-time
  validation alone doesn't stop **DNS rebinding**: a hostname can resolve to a public IP
  at registration time and a private one by the time a (re)delivery attempt runs minutes
  or hours later, since nothing pins the resolved address in between. Re-checking on every
  attempt closes that gap; a blocked attempt is recorded as a normal delivery failure
  (`"Blocked: URL resolves to a private, loopback, or otherwise disallowed network
  address."`) and goes through the same retry/backoff/`Failed` lifecycle as any other
  failure.

**Blocked:** loopback (`127.0.0.0/8`, `::1`), RFC 1918 private ranges (`10.0.0.0/8`,
`172.16.0.0/12`, `192.168.0.0/16`), link-local (`169.254.0.0/16` — which covers the
`169.254.169.254` cloud metadata endpoint — and IPv6 `fe80::/10`), IPv6 unique-local
(`fc00::/7`), carrier-grade NAT (`100.64.0.0/10`), multicast/reserved/broadcast, and any
host that fails to resolve at all (fails closed rather than treating "unknown" as
"allowed").

**Not covered, and worth knowing:** this checks the URL's resolved IP address, not what
happens after the TCP connection is established — it doesn't inspect HTTP redirects (a
public URL that 302s to a private one on the delivery `HttpClient` would need
`AllowAutoRedirect = false` plus a manual redirect-target re-check to close, not currently
implemented) or IP-based access control lists narrower than the ranges above (e.g. an
internal service the operator additionally wants excluded that happens to sit on a public
IP). Reasonable ranges for a general-purpose API; a deployment with unusually strict
requirements should treat this as a floor, not a ceiling.

Verified live: registering a webhook pointed at `127.0.0.1`, an RFC 1918 address, and
`169.254.169.254` each returned `422` with the expected message; a genuine public hostname
registered successfully.

---

## Testing it yourself

```bash
# 1. Register a webhook (requires an existing project's current ETag as If-Match)
curl -i -X POST https://localhost:5001/api/v1/projects/{id}/webhooks \
  -H 'If-Match: "AAAAAAAAB9E="' \
  -H 'Content-Type: application/json' \
  -d '{ "url": "https://webhook.site/your-id", "eventTypes": [], "registeredByUserId": "..." }'
# 201 Created

# 2. Trigger an event — e.g. move a task in that project
curl -i -X POST https://localhost:5001/api/v1/tasks/{taskId}/move \
  -H 'If-Match: "..."' \
  -H 'Content-Type: application/json' \
  -d '{ "status": "InProgress", "requestedByUserId": "..." }'

# 3. Within ~5-10s (one OutboxDispatcherJob tick + one WebhookDeliveryDispatcherService
#    tick), the registered URL receives:
#    { "event": "task.moved", "occurredAt": "...", "data": { "TaskId": "...", ... } }

# 4. List what's registered
curl -s https://localhost:5001/api/v1/projects/{id}/webhooks

# 5. Unregister
curl -i -X DELETE https://localhost:5001/api/v1/projects/{id}/webhooks/{webhookId} \
  -H 'If-Match: "..."'
# 204 No Content
```

Verified manually end to end during implementation against a real SQL Server instance and
a local HTTP receiver: `task.created` and `task.moved` both delivered with the expected
envelope shape, and a webhook pointed at an unreachable port correctly retried at the
30s/60s backoff schedule before landing in `Failed` with the error logged.

Unit coverage: `ProjectTests` (registration, the `MaxWebhooks` cap, unregistration,
archived-project guards), `RegisterWebhookValidatorTests`/`UnregisterWebhookValidatorTests`,
`RegisterWebhookHandlerTests`/`UnregisterWebhookHandlerTests`,
`GetProjectWebhooksHandlerTests`.
