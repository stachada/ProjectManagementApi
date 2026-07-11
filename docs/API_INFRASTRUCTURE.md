# Shared API infrastructure

`Ordinis.Api` wires up a small pipeline of cross-cutting middleware before any controller ever
runs. This doc explains what each piece is for and why it sits where it does in
[Program.cs](../src/Ordinis.Api/Program.cs):

```
correlation ID → request logging → global exception → status code pages → routing → auth (Phase 8) → endpoints
```

All of it lives in [src/Ordinis.Api/Common/](../src/Ordinis.Api/Common/).

---

## CorrelationIdMiddleware

Gives every HTTP request a unique ID that ties together everything that happens while handling
it — logs, error responses, even calls to downstream services — so you can trace one request's
full story after the fact.

Without it, a failure deep inside a handler just produces unlabeled log lines. Under concurrent
load those lines from different requests interleave, so there's no way to tell which lines belong
to which request. In production that makes debugging "user X reported an error at 3:04pm" nearly
impossible.

What it does, in order ([CorrelationIdMiddleware.cs](../src/Ordinis.Api/Common/CorrelationIdMiddleware.cs)):

1. **Resolve the ID** — if the caller already sent an `X-Correlation-ID` header (e.g. an upstream
   gateway, or another service chaining a call), reuse it. Otherwise generate a fresh
   `Guid.CreateVersion7()`. Reusing an inbound ID is what lets correlation IDs survive across
   service boundaries — if Service A calls Service B, both services' logs can be joined on the
   same value.
2. **Expose it two ways**: on `context.Items[...]` so other in-process code (notably
   `ProblemDetailsFactory`) can read it, and on the response header so the *client* gets it back
   too — a user reporting a failed request can hand you the correlation ID straight from their
   browser's network tab.
3. **Push it into the logger scope** via `logger.BeginScope(...)`, so every log line written
   anywhere during the request — by any handler, any middleware, EF Core — automatically carries
   `CorrelationId` as structured data without every log call needing to pass it explicitly.

It runs **first** in the pipeline because everything downstream (request logging, exception
handling) depends on the value it sets up.

---

## RequestLoggingMiddleware

Writes exactly one structured log line per request, at `Information` level, summarizing what
happened: method, path, status code, elapsed time, correlation ID
([RequestLoggingMiddleware.cs](../src/Ordinis.Api/Common/RequestLoggingMiddleware.cs)).

Individual handlers and libraries (EF Core, ASP.NET Core internals) already emit their own debug
noise. This middleware's job is to be the *one line you can grep for* to answer "was this request
slow? did it succeed? which correlation ID was it?" without wading through everything else.

It wraps the rest of the pipeline in a `Stopwatch` and logs in a `finally` block, so the summary
line is written whether the request succeeds, fails, or gets rewritten by
`GlobalExceptionMiddleware` — the `finally` guarantees it fires exactly once regardless of path.

It's registered **after** `CorrelationIdMiddleware` (so it can read the ID that was just set) and
**before** `GlobalExceptionMiddleware` (so by the time it logs `context.Response.StatusCode`, that
status code already reflects any exception translation — e.g. a thrown `NotFoundException` shows
up here as `404`, not as an unhandled exception).

---

## GlobalExceptionMiddleware

The single place that turns exceptions into HTTP responses, so individual controllers/handlers
never need a `try/catch` for these cases
([GlobalExceptionMiddleware.cs](../src/Ordinis.Api/Common/GlobalExceptionMiddleware.cs)):

| Exception | Status | Notes |
|---|---|---|
| `ValidationException` | `422` | Thrown by the `Dispatcher` when FluentValidation fails; field errors flow into a `ValidationProblemDetails` body |
| `NotFoundException` | `404` | Thrown by query/command handlers when a resource doesn't exist (or is soft-deleted) |
| `ConcurrencyException` | `409` | Thrown when EF Core detects a `RowVersion` mismatch — an optimistic concurrency conflict |
| `DomainException` | `422` | Thrown by aggregate methods when a business rule is violated (e.g. suspending an already-suspended organization); `ex.ErrorCode` flows into the response's `type` as `urn:ordinis:error:{ErrorCode}`, so clients can distinguish error causes that share the same status code |
| `BadHttpRequestException` | `ex.StatusCode` (typically `400`) | Thrown by Minimal API endpoints when a route/query parameter fails to bind (e.g. `GET /search?page=abc`) — caught in its own clause, *before* the catch-all, so it doesn't get flattened into a `500` |
| anything else | `500` | Logged with the full exception, but the client only ever sees a generic message — never the raw exception detail |

It wraps `await next(context)` in a `try/catch` chain and delegates response construction to
`ProblemDetailsFactory`, so this file stays focused on *which exception maps to which status code*
rather than on how a Problem Details body is shaped.

It sits **after** request logging (see above) and **before** routing, so it catches exceptions
thrown by anything downstream — routing, model binding, controller actions, and everything the
`Dispatcher` calls into.

**What it can't catch:** only *thrown exceptions*. Two classes of error reach the client without
ever throwing — see "Status code pages" and "ApiServiceExtensions" below for how those are covered.

---

## Status code pages (`app.UseStatusCodePages(...)`)

An unmatched route (`404` — routing never found a matching endpoint) or a disallowed HTTP method
(`405` — an endpoint matched a different verb) reaches the client with an empty body. No exception
is thrown in either case, so `GlobalExceptionMiddleware`'s `try/catch` never runs — there's nothing
for it to catch.

`UseStatusCodePages` (registered inline in `Program.cs`, not its own file) plugs that gap: after the
rest of the pipeline runs, it checks whether the response ended with a `400`–`599` status *and* no
body has been written yet. If both hold, it invokes a callback that builds a Problem Details body via
`ProblemDetailsFactory.Create`, keyed off the status code, so these responses look like every other
error response instead of an empty `404`/`405`.

That "no body written yet" check is also what makes it safe to layer on top of the exception-driven
`404`s `GlobalExceptionMiddleware` already produces for `NotFoundException` — those responses have
already written a body by the time `UseStatusCodePages` inspects them, so its callback never fires
for them; it only fires for the truly bodyless cases.

It's registered **after** `GlobalExceptionMiddleware` and **before** routing: after, so a `404`
`GlobalExceptionMiddleware` already answered isn't touched a second time; before routing, so it wraps
everything that could produce a routing-level `404`/`405` in the first place.

---

## ProblemDetailsFactory

A static helper that builds RFC 9457 Problem Details bodies with one consistent shape, so every
error response — regardless of which exception produced it — looks the same to API consumers
([ProblemDetailsFactory.cs](../src/Ordinis.Api/Common/ProblemDetailsFactory.cs)).

Three entry points:

- `Create(...)` — a plain `ProblemDetails` for single-message errors (404, 405, 409, 500).
- `CreateValidation(...)` — a `ValidationProblemDetails` (`422`), carrying FluentValidation's
  field-keyed error dictionary directly in the response body.
- `CreateModelBindingValidation(...)` — a `ValidationProblemDetails` (`400`), carrying
  ASP.NET Core's own `ModelState` errors (a malformed request body, a route/query value that can't
  convert to its target type). Kept as a separate `400` entry point rather than reusing
  `CreateValidation`'s `422`: a request the framework can't even bind is malformed, which is a
  different failure class from a request that binds fine but violates a business rule.

All three attach the current request's correlation ID as a `correlationId` extension field
(read from `context.Items`, set earlier by `CorrelationIdMiddleware`), which is what fulfills the
"add `CorrelationId` to all Problem Details responses" requirement — the ID that shows up in your
server logs is the same one the client sees in the error body, so a bug report with a Problem
Details response pasted into it is immediately traceable to a log line.

It's kept separate from `GlobalExceptionMiddleware` so the *shape* of an error response
(the factory) and the *policy* of which exception maps to which status (the middleware) can
change independently.

---

## ApiServiceExtensions

`AddApiServices(this IServiceCollection)` — the API-layer counterpart to
`AddApplicationServices` and `AddInfrastructureServices`, called once from `Program.cs`
([ApiServiceExtensions.cs](../src/Ordinis.Api/Common/ApiServiceExtensions.cs)). It registers:

- **Controllers** (`AddControllers()`), with `ConfigureApiBehaviorOptions` overriding
  `InvalidModelStateResponseFactory` (see below).
- **Response caching** (`AddResponseCaching()`) — enables `[ResponseCache]` attributes and
  `Cache-Control` header handling on endpoints added in later phases.
- **CORS** — a permissive default policy (any origin/method/header). Fine for a portfolio project
  with no browser-based first-party client yet; would need tightening before fronting a real SPA.
- **Rate limiting** — a global fixed-window limiter (100 requests/minute), partitioned per client
  IP address so one noisy caller only throttles itself, not everyone else. Rejected requests get
  `429 Too Many Requests`.

Keeping this in one extension method (rather than scattering `AddX()` calls across `Program.cs`)
matches the pattern already used for `AddApplicationServices` and `AddInfrastructureServices` —
`Program.cs` stays a short, readable list of "which layers are wired up," not a dumping ground for
every individual service registration.

### `ConfigureApiBehaviorOptions` / `InvalidModelStateResponseFactory`

`[ApiController]` runs model binding and validation *before* an action method is ever invoked. When
`ModelState.IsValid` is `false`, it short-circuits the request right there and builds the `400`
response itself — this happens entirely outside `GlobalExceptionMiddleware`'s `try/catch`, since
nothing throws. Left at its default, that response is ASP.NET Core's own `ValidationProblemDetails`
shape: no `correlationId`, a generic RFC 9110 `type` URI instead of this API's
`https://httpstatuses.io/{status}` convention — inconsistent with every other error response in the
API.

`ConfigureApiBehaviorOptions` is the seam `AddControllers()` exposes for overriding `[ApiController]`'s
built-in behaviors. The override here replaces `InvalidModelStateResponseFactory`: it flattens
`ModelState` into the same `field → string[]` shape FluentValidation errors already flow through,
hands it to `ProblemDetailsFactory.CreateModelBindingValidation`, and wraps the result in an
`ObjectResult` so MVC still serializes it as the action result — just with this API's body, status
code, and `application/problem+json` content type instead of the framework's defaults.

---

## DataShaper

Unlike everything above, `DataShaper` isn't wired into the middleware pipeline — it's a static
helper controller actions call directly, one line per list endpoint
([DataShaper.cs](../src/Ordinis.Api/Common/DataShaping/DataShaper.cs)).

**The problem it solves:** every list endpoint's DTO (`TaskSummaryDto`, `ProjectSummaryDto`, ...)
returns a fixed set of fields. A client that only needs `id` and `title` — a mobile list view, a
bandwidth-conscious integration — still pays for the whole payload: every field, every task, every
page. The REST-y answer is a sparse fieldset: `?fields=id,title,status` trims the response to just
those fields. Hand-writing that per DTO would mean either a combinatorial explosion of narrower DTO
types (one per field combination a client might ask for) or an `if/else` chain of manual projections
in every list action — neither scales past a couple of endpoints, let alone the seven this project
has. `DataShaper` solves it once, generically, and every list endpoint gets it for free.

**How it works:**

1. `ShapeCollection<T>(IEnumerable<T> source, string? fields)` / `ShapeItem<T>(T source, string?
   fields)` are the two entry points a controller calls, e.g.
   `Ok(DataShaper.ShapeCollection(result.Items, fields))`. They used to be two overloads of the same
   name (`ShapeData`), which caused a real bug — see "Bugs found" below.
2. `GetProperties<T>(fields)` reflects `typeof(T).GetProperties(...)` once and, if `fields` was
   supplied, filters that list down to the properties whose name matches one of the comma-separated
   names case-insensitively, preserving the caller's requested order. Unknown names are silently
   dropped rather than erroring — a client typo shouldn't take down the whole response, it just
   won't see that field. `Id` is always force-included even if the caller didn't ask for it, so a
   shaped resource stays addressable (a client that trimmed away `id` could never look the resource
   back up).
3. `Shape<T>(source, properties)` builds one `ExpandoObject` per item, populating it via
   `property.GetValue(source)` for each surviving `PropertyInfo`, with the key run through
   `JsonNamingPolicy.CamelCase.ConvertName(...)` so a shaped response's casing matches every
   unshaped DTO response (MVC's default `System.Text.Json` output is camelCase).
4. `ExpandoObject` implements `IDictionary<string, object?>`, which `System.Text.Json` serializes as
   a plain JSON object containing exactly the keys that were set — that's the mechanism that lets a
   single method return a *differently shaped* object per request without a matching C# type for
   every possible field combination.

This lives at the API layer, not in `Ordinis.Application`'s manual DTO mappers
(`TaskMapper`, `ProjectMapper`, ...). Sparse fieldsets are a response-*shaping* concern (which
fields does *this HTTP response* include), not a mapping concern (how does *this entity* become
*this DTO*) — keeping it here means the manual mappers stay untouched, reflection-free, and fully
typed, exactly as the project's mapping conventions call for.

**Bugs found during manual verification** (both documented in `BUILD_PLAN.md`'s Phase 6 entry):

- **Ambiguous overload resolution.** With both entry points originally named `ShapeData<T>`,
  calling `ShapeData(result.Items, fields)` — `result.Items: IReadOnlyList<TaskSummaryDto>` — bound
  to the *single-item* overload (`ShapeData<T>(T source, ...)`) instead of the collection one,
  because C# prefers an identity conversion (`T = IReadOnlyList<TaskSummaryDto>`) over the interface
  conversion the collection overload would have needed. That reflected the *list itself*, and
  `property.GetValue(source)` threw `TargetParameterCountException` the moment it hit the list's
  `this[int]` indexer property. Renaming to distinct `ShapeCollection`/`ShapeItem` names removes the
  ambiguity outright — no clever type constraint needed, just names that can't collide.
- **Casing inconsistency.** Before the `JsonNamingPolicy.CamelCase.ConvertName` call was added,
  `ExpandoObject` keys came straight from `PropertyInfo.Name` — PascalCase — while every other
  endpoint's DTOs serialize camelCase. A client toggling `?fields=` on and off would have seen the
  same resource's keys change case depending on whether the query parameter was present.

---

## Serilog

Configured in `Program.cs` via `builder.Host.UseSerilog(...)`, reading levels and sinks from the
`Serilog` section of `appsettings.json` / `appsettings.{Environment}.json` (see
[appsettings.json](../src/Ordinis.Api/appsettings.json)). Packages live in `Ordinis.Api` only —
`Ordinis.Infrastructure` and `Ordinis.Application` depend on the generic `Microsoft.Extensions.Logging`
abstractions (`ILogger<T>`), never on Serilog directly, so the concrete logging framework stays a
composition-root concern, swappable without touching any other layer.

`.Enrich.FromLogContext()` is what makes `CorrelationIdMiddleware`'s `logger.BeginScope(...)` call
actually flow through to the Serilog sink output — without it, scope values are captured by
`Microsoft.Extensions.Logging` but Serilog's console/file sinks would ignore them.
