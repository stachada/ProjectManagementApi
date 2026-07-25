# Idempotency — `Idempotency-Key`

This project protects the eight create-style `POST` endpoints from duplicate submissions
using the standard `Idempotency-Key` header pattern: a client generates a unique key per
logical operation, sends it on every attempt (including retries), and the server replays
the first successful response instead of re-running the operation a second time.

This doc explains the full round trip end to end. For where `IdempotencyMiddleware` sits
in the request pipeline relative to the API's other middleware, see
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#idempotencymiddleware).

---

## Why this exists

`POST` isn't safe or idempotent by HTTP's own rules — retrying one after a timeout or a
dropped connection risks creating the resource twice:

1. Client sends `POST /api/v1/organizations` with `{ "name": "Acme Corp" }`.
2. The server creates the organization and starts writing the response, but the
   connection drops before the client receives it.
3. The client, having no idea whether the request succeeded, retries the exact same
   `POST`.
4. Without protection, this creates a second `Acme Corp` organization — the client has no
   way to tell "my retry created a duplicate" from "my retry safely got me the same
   result."

`Idempotency-Key` fixes this: the client attaches the same key to both attempts, and the
server recognizes the second one as a replay rather than a new operation.

This is a different problem from the one [CONCURRENCY.md](CONCURRENCY.md) solves.
ETags/`If-Match` protect *updates* to a resource that already exists (lost-update
prevention). `Idempotency-Key` protects *creates*, where there's no prior version to
compare against in the first place.

---

## The key

The client generates the key, not the server — typically a UUID minted once per logical
operation (e.g. once per "Create Organization" form submission, reused across retries of
that same submission, not regenerated per HTTP attempt). This API places no format
requirement on it beyond "non-empty string"; it's opaque as far as the server is
concerned.

```http
POST /api/v1/organizations
Idempotency-Key: 0198f3c2-9e2b-7c31-8b2a-4e6f1a2d3c4b
Content-Type: application/json

{ "name": "Acme Corp", "description": "..." }
```

Omitting the header entirely is always valid — it simply opts a request out of replay
protection, falling back to plain non-idempotent `POST` behavior. This matters for
clients that don't need the guarantee (a script running once, a test) as much as for
ones that do.

---

## Step by step

### 1. `IdempotencyMiddleware` checks whether the request is in scope

(`src/Ordinis.Api/Common/IdempotencyMiddleware.cs`) — see
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#idempotencymiddleware) for its place in the
pipeline. On every request, it passes straight through to the rest of the pipeline,
untouched, unless **all** of the following hold:

- The method is `POST`.
- The matched endpoint carries an `[Idempotent]` attribute (`IdempotentAttribute.cs`).
- An `Idempotency-Key` header is present and non-blank.

This mirrors `ConcurrencyTokenMiddleware`'s "dumb extractor" convention — the middleware
itself has no per-route policy baked in; it only reacts to whether `[Idempotent]` is
present on the endpoint it's asked to process.

### 2. The middleware builds a cache key and hashes the request body

```csharp
string cacheKey = $"{context.Request.Method}:{context.Request.Path}:{idempotencyKey}";
```

Scoping the cache key by method and path — not just the raw header value — means the
same key string accidentally reused across two different endpoints can't collide with
each other's cached responses.

The request body is read as raw bytes (via `Request.EnableBuffering()` +
`CopyToAsync` into a buffer, then `Position = 0` so model binding downstream still
works) and hashed with SHA-256. Raw bytes, not text — `AddAttachment`'s body is
`multipart/form-data` containing binary file content, and decoding it as UTF-8 text then
re-encoding for hashing could silently corrupt invalid byte sequences before they ever
reach the hash function.

### 3. A cache lookup decides what happens next

`IIdempotencyStore.TryGetAsync(cacheKey)` (`src/Ordinis.Application/Common/IIdempotencyStore.cs`)
has three possible outcomes:

**Miss** — no prior request used this key on this route. The middleware buffers
`context.Response.Body` into a `MemoryStream`, calls the rest of the pipeline (routing →
model binding → the controller action → the `Dispatcher` → the handler), then:

- If the resulting status code is `< 300`, caches `(status, Content-Type, Location,
  body bytes)` under the cache key for 24 hours.
- Either way, copies the buffered bytes to the real response stream so the caller gets a
  normal response either way.

**Hit, body hash matches** — the same operation is being retried with the same payload.
The middleware writes the cached status code, `Content-Type`, `Location` header, and body
straight to the response and returns **without calling the rest of the pipeline at all**.
The handler never runs a second time — no duplicate row, no duplicate side effect.

**Hit, body hash differs** — the key was reused, but the request body doesn't match what
was cached under it the first time. This is a client bug, not a safe retry: silently
replaying the old response would return the wrong data, and silently creating a second
resource would defeat the whole point of sending the key. The middleware throws
`IdempotencyKeyConflictException`.

### 4. `GlobalExceptionMiddleware` maps a key conflict to `409 Conflict`

Same mechanism as every other domain/application exception in this API — see
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#globalexceptionmiddleware)'s exception
table. `409` was chosen over inventing a new status code because it's already this API's
"conflicts with prior state tied to an identifier" status — the same one
`ConcurrencyException` uses for a stale `RowVersion`.

### 5. Only successful responses are ever cached

A failed attempt — `422` (validation failure), `404`, `500`, anything `>= 300` — is
**never** written to the store. This is deliberate, for two reasons:

- **The client should be able to fix and retry.** If a `422` got cached, a client that
  sent an invalid body, then corrected it and retried under the *same* key, would just
  get the original `422` replayed forever instead of a fresh attempt.
- **`ProblemDetailsFactory` bakes in a `correlationId`** that belongs to the *originating*
  request (`ProblemDetailsFactory.AddCorrelationId`, reading `CorrelationIdMiddleware`'s
  `HttpContext.Items` entry). Replaying a cached error body verbatim on a later retry
  would show that stale correlation ID on a response tied to a completely different
  underlying HTTP request — actively misleading for anyone trying to trace a bug report
  back to a log line.

---

## Storage

`IIdempotencyStore` (`src/Ordinis.Application/Common/IIdempotencyStore.cs`) is a pure
contract:

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken);
}
```

The only implementation today, `InMemoryIdempotencyStore`
(`src/Ordinis.Infrastructure/Common/InMemoryIdempotencyStore.cs`), wraps `IMemoryCache`
and is registered as a singleton in `InfrastructureServiceExtensions.AddInfrastructureServices`.
This mirrors the existing `IFileStorageService` swap-friendly pattern used for local file
storage — a future multi-instance deployment can introduce a Redis-backed
`IIdempotencyStore` in `Ordinis.Infrastructure` without touching the middleware, the
attribute, or any controller.

**Known limitation:** because the store is in-memory and per-instance, the replay
guarantee only holds within a single running process. A restart clears the cache (any
key becomes "unseen" again), and a multi-instance deployment behind a load balancer would
only catch a duplicate if the retry happens to land on the same instance. Acceptable for
this project's current single-instance deployment target; the interface exists
specifically so this can be upgraded later without a design change.

Cache expiry runs on the wall clock — `MemoryCacheOptions` has no `TimeProvider`-based
clock override in the referenced `Microsoft.Extensions.Caching.Memory` package version
(10.0.9), so this is the one clock dependency in the codebase not routed through the
app's `TimeProvider` singleton.

---

## What's guarded and what isn't

| Route | Guarded |
|---|---|
| `POST /api/v1/tasks` | ✅ |
| `POST /api/v1/tasks/{id}/comments` | ✅ |
| `POST /api/v1/tasks/{id}/attachments` | ✅ |
| `POST /api/v1/projects` | ✅ |
| `POST /api/v1/projects/{id}/members` | ✅ |
| `POST /api/v1/projects/{projectId}/boards` | ✅ |
| `POST /api/v1/organizations` | ✅ |
| `POST /api/v1/users` | ✅ |
| Everything else (`move`, `assign`, `unassign`, `close`, `reopen`, `archive`,
  `unarchive`, `suspend`, `reactivate`, `deactivate`, ...) | ❌ |

Only the eight create-style actions carry `[Idempotent]`. State-transition endpoints are
excluded deliberately — they're not creating a new resource, so a duplicate submission
doesn't produce a duplicate row the way a `POST /tasks` retry would; at worst it's a
redundant no-op state change, and several of them (`move`, `assign`) already have their
own domain-level guards (e.g. the state machine rejects an already-applied transition).
Extending `[Idempotent]` there was considered out of scope for this phase, matching how
`CONCURRENCY.md`'s `If-Match` guard also stops short of covering every mutating endpoint.

---

## Testing it yourself

```bash
# 1. First attempt — creates the organization
curl -i -X POST https://localhost:5001/api/v1/organizations \
  -H 'Idempotency-Key: 0198f3c2-9e2b-7c31-8b2a-4e6f1a2d3c4b' \
  -H 'Content-Type: application/json' \
  -d '{ "name": "Acme Corp" }'
# 201 Created, Location: /api/v1/organizations/{id}

# 2. Retry with the same key and body — replayed, not re-executed
curl -i -X POST https://localhost:5001/api/v1/organizations \
  -H 'Idempotency-Key: 0198f3c2-9e2b-7c31-8b2a-4e6f1a2d3c4b' \
  -H 'Content-Type: application/json' \
  -d '{ "name": "Acme Corp" }'
# 201 Created, identical body and Location — only one organization exists in the DB

# 3. Reuse the same key with a different body — rejected
curl -i -X POST https://localhost:5001/api/v1/organizations \
  -H 'Idempotency-Key: 0198f3c2-9e2b-7c31-8b2a-4e6f1a2d3c4b' \
  -H 'Content-Type: application/json' \
  -d '{ "name": "Something Else" }'
# 409 Conflict, Problem Details body

# 4. Omit the header entirely — normal, non-idempotent behavior
curl -i -X POST https://localhost:5001/api/v1/organizations \
  -H 'Content-Type: application/json' \
  -d '{ "name": "Acme Corp" }'
# 201 Created — a second, genuinely new organization (unless the duplicate-slug validator
# rejects it, which is a separate, unrelated 422 not related to idempotency at all)
```

`tests/Ordinis.IntegrationTests/Common/IdempotencyMiddlewareTests.cs` covers all four of
these outcomes against `POST /api/v1/organizations`, plus a same-key-after-a-failed-attempt
case proving a `422` doesn't get cached.
