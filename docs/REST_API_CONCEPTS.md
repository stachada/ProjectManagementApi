# RESTful API design concepts

This project exists to demonstrate REST API design patterns beyond basic CRUD. This doc
is a guided tour of *why* each pattern exists as a general HTTP/REST concept, and a
short pointer to *how* Ordinis implements it — full implementation detail lives in a
dedicated doc per feature where one exists (`CONCURRENCY.md`, `IDEMPOTENCY.md`,
`API_INFRASTRUCTURE.md`); this doc doesn't repeat that detail, it links to it.

Status markers below reflect the actual state of `BUILD_PLAN.md`'s Phase 7 checklist,
not aspiration — some of these are fully built, some partially, some not yet started.

| Status | Meaning |
|---|---|
| ✅ | Implemented |
| 🚧 | Partially implemented |
| ⏳ | Not started |

---

## HATEOAS ✅

**Idea:** Hypermedia As The Engine Of Application State — a REST maturity concept from
Roy Fielding's dissertation, and the top level of the
[Richardson Maturity Model](https://martinfowler.com/articles/richardsonMaturityModel.html).
Instead of a client hardcoding "to close a task, `POST` to `/tasks/{id}/close`," the
*server's response* tells the client which actions are currently valid and how to invoke
them. The client only needs to know the resource's initial URL (an entry point); every
subsequent action is discovered from the representation itself, the same way a human
browsing a website follows links instead of memorizing URLs.

**Why it matters beyond "it's more RESTful":** a task's legal actions genuinely change
based on server-side state. A `Cancelled` task has no legal transitions at all; a `Done`
task can only reopen. Hardcoding "which buttons are enabled" into every client
duplicates the state machine that already lives in `ProjectTaskStatusExtensions`. HATEOAS
means the server is the single source of truth for "what can happen next," and a new
client (a future mobile app, a CLI, a partner integration) gets that logic for free just
by reading the response — no separate state-machine reimplementation required, and no
version-skew risk between a client's copy of the rules and the server's.

**How Ordinis does it:** every `TaskDto` and `ProjectDto` embeds a `_links` array —
`HateoasLink { Rel, Href, Method }` records
(`src/Ordinis.Application/Common/HateoasLink.cs`), built by the manual mappers
(`TaskMapper.ToDto`, `ProjectMapper.ToDto`) with no `IUrlHelper`/`LinkGenerator`
dependency, keeping them pure functions.

```json
{
  "id": "0198f3c2-...",
  "title": "Fix login bug",
  "status": "InProgress",
  "_links": [
    { "rel": "self",          "href": "/api/v1/tasks/0198f3c2-...",       "method": "GET" },
    { "rel": "assign",        "href": "/api/v1/tasks/0198f3c2-.../assign", "method": "POST" },
    { "rel": "delete",        "href": "/api/v1/tasks/0198f3c2-...",       "method": "DELETE" },
    { "rel": "move",          "href": "/api/v1/tasks/0198f3c2-.../move",   "method": "POST" },
    { "rel": "move:done",     "href": "/api/v1/tasks/0198f3c2-.../move",   "method": "POST" },
    { "rel": "move:cancelled","href": "/api/v1/tasks/0198f3c2-.../move",   "method": "POST" }
  ]
}
```

`self`, `assign`, and `delete` are always present on `TaskDto`; `move` plus one
`move:{status}` per legally reachable transition are driven directly by
`ProjectTaskStatusExtensions.GetAllowedTransitions()` — a `Cancelled` task (no outbound
transitions) gets no `move*` links at all, since advertising an action that would always
fail `422` isn't useful. `ProjectDto` gets a simpler fixed set: `self`, `tasks`,
`boards`, `members`, `delete`.

**Deliberately not everywhere:** list views (`TaskSummaryDto`, `ProjectSummaryDto`) carry
no `_links` — every item in a 100-row page repeating five link objects is a real
bandwidth cost for a client that's about to `GET` the detail view anyway to get anything
useful done with the resource. This is a common, accepted HATEOAS trade-off: full
hypermedia on detail views, lean payloads on list views. `BoardDto` doesn't have
`_links` yet either — not a deliberate exclusion, just not extended there this phase.

---

## Optimistic concurrency (ETag / If-Match) ✅

**Idea:** HTTP's standard mechanism for preventing *lost updates* — two clients editing
the same resource, where the second write silently overwrites the first client's change
because neither client knew about the other's write. A server hands out an opaque
version token (`ETag`) with every read; a client must echo it back (`If-Match`) on any
write, and the server rejects the write (`412 Precondition Failed` per the HTTP spec, or
in this API's case `409 Conflict` — see the doc below for why) if the resource has
changed since that token was issued.

This is different from *idempotency* below, even though both use a "compare something
the client sends against server state" pattern: concurrency control protects **updates**
to a resource that already exists; idempotency protects **creates**, where there's no
prior version to compare against in the first place.

**How Ordinis does it:** full mechanism — the `RowVersion` token, `ConcurrencyTokenMiddleware`,
`ConcurrencyGuard`, the proactive-vs-reactive check split, and exactly which endpoints are
guarded — is documented end to end in **[CONCURRENCY.md](CONCURRENCY.md)**.

---

## Idempotency (`Idempotency-Key`) ✅

**Idea:** `POST` is neither safe nor idempotent under HTTP's own rules — sending it twice
can create two resources. A client that retries after a timeout or dropped connection has
no way to tell "my retry safely got me the same result" from "my retry just created a
duplicate." The `Idempotency-Key` header (the same pattern used by Stripe, PayPal, and
most payment/messaging APIs) fixes this: the client generates one key per logical
operation, sends it on every attempt including retries, and the server recognizes a
repeat as a replay rather than a new operation.

**How Ordinis does it:** the full request/response caching mechanism, conflict detection
on a reused key with a different body, and exactly which endpoints are covered, is
documented end to end in **[IDEMPOTENCY.md](IDEMPOTENCY.md)**.

---

## Problem Details (RFC 9457) ✅

**Idea:** [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html) standardizes the *shape*
of an HTTP API error response — `type`, `title`, `status`, `detail`, `instance` — so a
client can parse any error from any RFC-9457-compliant API the same way, instead of every
API inventing its own ad-hoc `{ "error": "..." }` JSON shape. It's the machine-readable
counterpart to using standard HTTP status codes in the first place: a status code alone
tells a client "this failed," Problem Details tells it *why*, in a structured,
programmatically-consumable form.

**How Ordinis does it:** every error response — a thrown `ValidationException`,
`NotFoundException`, `ConcurrencyException`, `IdempotencyKeyConflictException`,
`DomainException`, or an unhandled `500` — is translated into a Problem Details body by
`GlobalExceptionMiddleware` + `ProblemDetailsFactory`, with a project-specific extension:
every response also carries a `correlationId` field tying the error back to a specific
server log line. Full exception→status mapping table and the two bodyless-error cases
(`404`/`405` routing failures with no thrown exception) are documented in
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#globalexceptionmiddleware).

---

## Pagination, filtering, and sorting ✅

**Idea:** a collection endpoint that always returns every row doesn't scale — a `GET
/tasks` against a board with ten thousand tasks would be enormous, slow, and mostly
wasted if the client only wants to show 20 at a time. Pagination caps response size;
filtering lets the client ask the server to narrow the set server-side (cheaper and more
correct than fetching everything and filtering client-side); sorting makes "page 3" mean
something stable and repeatable.

**How Ordinis does it:** offset-based pagination (`?page=`, 1-based, default 1; `?pageSize=`,
default 20, server-clamped to a max of 100) on every list endpoint, returning a
`PagedResult<T>` (`Ordinis.Application/Common/PagedResult.cs`) and an `X-Total-Count`
response header carrying the total row count across all pages (so a client can compute
total page count without a second request). Sorting via `?sortBy=`/`?sortDescending=`,
with an unrecognized `sortBy` value falling back to a safe default rather than erroring.
Filtering is resource-specific query params (`?boardId=`, `?assigneeId=`, `?status=`,
`?priority=`, `?dueBefore=`, `?dueAfter=` on tasks; `?organizationId=`, `?memberId=`,
`?includeArchived=` on projects; and so on).

One easy-to-miss correctness detail: every `OrderBy`/`OrderByDescending` in this codebase
ends with `.ThenByStableId()` (`EntityQueryableExtensions`). SQL gives no guaranteed
ordering for ties on the primary sort key (e.g. two tasks created in the same
millisecond) — without a deterministic tiebreaker, which rows land on which page can
silently vary between two otherwise-identical requests. This is enforced as a project
convention in `CLAUDE.md`, not just a one-off fix.

---

## Sparse fieldsets (`?fields=`) ✅

**Idea:** a client that only needs `id` and `title` for a dropdown still pays, over the
wire, for every field of every DTO on every row of every page by default. Sparse
fieldsets let a client opt into a narrower response shape per request:
`?fields=id,title,status` trims each returned object down to just those fields.

**How Ordinis does it:** `DataShaper.ShapeCollection<T>`/`ShapeItem<T>`
(`src/Ordinis.Api/Common/DataShaping/DataShaper.cs`) reflect over the DTO's public
properties once per call, filter to the requested (case-insensitive, order-preserving)
names, and build an `ExpandoObject` per item so `System.Text.Json` serializes exactly the
requested keys — no combinatorial explosion of narrower DTO types, and no per-endpoint
`if/else` projection logic. `Id` is always force-included, even if not requested, so a
shaped resource never loses its own address. Full writeup, including two real bugs found
building it, in [API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#datashaper).

---

## Rate limiting ✅

**Idea:** an API with no request cap is one slow client, one runaway retry loop, or one
malicious actor away from degrading service for everyone else. Rate limiting rejects
excess requests (`429 Too Many Requests`, with a `Retry-After` header telling the client
when to try again) before they consume real backend resources.

**How Ordinis does it:** a single global limiter, wired up in
`ApiServiceExtensions.AddApiServices()` (`services.AddRateLimiter(...)`) and applied via
`app.UseRateLimiter()` in `Program.cs`, picks one of two policies per request via
`RateLimitPartitioner.CreatePartition` (`src/Ordinis.Api/Common/RateLimitPartitioner.cs`):

- **Anonymous requests** (`HttpContext.User.Identity?.IsAuthenticated == false`) — a fixed
  window, 100 requests/minute, partitioned per client IP so one noisy caller only throttles
  itself.
- **Authenticated requests** — a sliding window, 500 requests/minute, partitioned per user
  ID (`ClaimTypes.NameIdentifier`). This branch is dormant today: Phase 8 (JWT auth) hasn't
  landed yet, so `HttpContext.User` is never authenticated and every request currently takes
  the anonymous branch. It activates automatically once Phase 8 wires up JWT auth, with no
  further changes to the limiter itself.

Both limits are config-driven (`RateLimiting` section of `appsettings.json`) rather than
hardcoded. Rejected requests get a real `429 Too Many Requests`: the limiter's `OnRejected`
callback reads the algorithm's own `Retry-After` metadata and sets it as a response header,
then writes a body through the same `ProblemDetailsFactory` every other error response uses
— RFC 9457 shape, `correlationId` included — instead of the default plain-text rejection.
`GET /health` is exempted via `.DisableRateLimiting()`, since orchestrator liveness/readiness
probes poll frequently and shouldn't compete with real traffic for the same budget.

Full pipeline placement and design notes: [API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#apiserviceextensions).

---

## Response caching

**Idea:** a `GET` whose result rarely changes (or is explicitly allowed to be briefly
stale) shouldn't force a full round trip to the database on every request. `Cache-Control`
response headers tell HTTP-aware clients and intermediary caches (browsers, CDNs, reverse
proxies) how long a response may be reused without re-asking the server, trading a small
staleness window for a large reduction in server load.

**How Ordinis does it:** the ASP.NET Core Response Caching *service*
(`services.AddResponseCaching()` in `ApiServiceExtensions`, `app.UseResponseCaching()` in
`Program.cs`) pairs with the built-in `[ResponseCache]` action attribute — no custom
middleware needed, since this is exactly the scenario that middleware exists for. Every
single-resource `GetById` action (`TasksController`, `TasksV2Controller`, `ProjectsController`,
`BoardsController`, `OrganizationsController`, `UsersController`) carries
`[ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept-Encoding,Authorization")]`,
producing `Cache-Control: public, max-age=30` and `Vary: Accept-Encoding, Authorization` on a
`200` response.

**Scope: single-resource GETs only, not list/collection endpoints.** The `GetById` actions are
also the ones carrying `ConcurrencyToken` → `ETag` (see "Optimistic concurrency" above), so a
short cache window pairs naturally with a resource that already has its own
staleness signal. List/collection endpoints (`GET /tasks`, `GET /projects/{id}/tasks`, etc.)
deliberately stay uncached: there's no cache-invalidation mechanism in this codebase yet, and a
stale cached list is a more visible correctness problem — a task that just moved boards still
showing in the old board's list — than a stale single resource for 30 seconds.

**`Vary: ... Authorization`, ahead of auth existing.** Phase 8 (JWT auth) isn't implemented yet,
so every request today is anonymous and this header currently has no observable effect. It's
included now because the built-in `[ResponseCache]` attribute makes it free to declare
up front, and once `[Authorize]` lands, per-user responses served from the same route won't get
cross-contaminated by a shared cache entry — the header's job is done at declaration time, not
retrofitted later.

---

## API versioning ✅

**Idea:** a REST API that will ever need a breaking change needs a way to serve both the
old and new contract simultaneously, so existing clients don't break the moment a new
version ships. URL-segment versioning (`/api/v1/...` vs `/api/v2/...`) is the simplest,
most explicit strategy — a client's chosen version is visible in every request URL, no
custom header or content-negotiation parsing required.

**How Ordinis does it:** every route carries a literal `api/v1/` prefix
(`[Route("api/v1/tasks")]` and so on), and `TasksV2Controller` (`[Route("api/v2/tasks")]`)
adds a `GET /api/v2/tasks/{id}` endpoint alongside it to demonstrate a second, coexisting
API version. v2 dispatches the same `GetTaskById` query and reuses `TaskDto`/`TaskMapper`
unchanged, then appends one extra `board` `HateoasLink` to the response's `_links` —
`TaskDto` carries `BoardId` but v1 never exposed a direct hyperlink to the parent board
despite already linking `self`/`assign`/`delete`/`move`, so this is a genuine, useful
v1/v2 difference rather than a contrived one.

**Deliberately not done:** no `Asp.Versioning` (or similar) NuGet package is installed —
versioning stays a hardcoded route-string convention, the same one every v1 controller
already used, rather than a formal `[ApiVersion]`-attribute mechanism. That's a reasonable
trade for a single demonstration endpoint on top of a codebase that already avoids
framework abstraction elsewhere (no MediatR, no AutoMapper). Worth revisiting if this
API ever grows a real v3, needs version negotiation beyond the URL segment (e.g. an
`Accept` header or query string), or needs to advertise per-endpoint deprecation —
`Asp.Versioning.Mvc` covers all three out of the box and would replace the hardcoded
prefixes with `[ApiVersion]` attributes at that point.

---

## Webhooks ⏳

**Idea:** polling an API for changes ("did anything happen since I last checked?") wastes
requests when nothing changed and adds latency when something did. Webhooks invert the
relationship — the server proactively `POST`s an event payload to a URL the client
registered in advance, the moment something happens, so integrations react in near
real-time without polling at all.

**Current state:** not started. The plan calls for `POST/DELETE
/projects/{id}/webhooks` registration endpoints (Minimal API, not a controller — matching
this project's "Minimal APIs for non-resource routes" convention) and a
`WebhookDispatcherService` subscribing to the existing `OutboxMessage` events
(`TaskCreated`, `TaskMoved`, `TaskAssigned`, `CommentAdded`) to fire outbound HTTP calls
with basic retry. The Outbox infrastructure this would build on (Phase 5) already exists;
the webhook-specific layer on top of it doesn't yet.

---

## Audit log ⏳

**Idea:** "what happened to this resource, by whom, and when" is a common requirement for
any collaborative tool — not just for debugging, but for accountability (who moved this
task to Done?) and compliance. An audit log endpoint exposes the system's own event
history as a first-class, queryable API rather than something only visible by reading raw
database rows or log files.

**Current state:** not started. The plan calls for `GET /projects/{id}/audit`, backed by
the same `OutboxMessages` table the Outbox dispatcher already writes to (no separate
audit store needed), and is earmarked as this project's first genuine multi-table Dapper
query (`OutboxMessages` → `Tasks`/`Boards` → `Project`), since everything so far has been
straightforward single-table EF Core LINQ.

---

## Further reading

- [CONCURRENCY.md](CONCURRENCY.md) — ETag/If-Match, full mechanism
- [IDEMPOTENCY.md](IDEMPOTENCY.md) — `Idempotency-Key`, full mechanism
- [API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md) — middleware pipeline, Problem Details,
  sparse fieldsets, and everything else cross-cutting
- [BUILD_PLAN.md](../BUILD_PLAN.md) — Phase 7 checklist and the design decisions behind
  each feature as it was actually built
