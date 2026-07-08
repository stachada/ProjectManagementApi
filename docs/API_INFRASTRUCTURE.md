# Shared API infrastructure

`Ordinis.Api` wires up a small pipeline of cross-cutting middleware before any controller ever
runs. This doc explains what each piece is for and why it sits where it does in
[Program.cs](../src/Ordinis.Api/Program.cs):

```
correlation ID → request logging → global exception → routing → auth (Phase 8) → endpoints
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
| anything else | `500` | Logged with the full exception, but the client only ever sees a generic message — never the raw exception detail |

It wraps `await next(context)` in a `try/catch` chain and delegates response construction to
`ProblemDetailsFactory`, so this file stays focused on *which exception maps to which status code*
rather than on how a Problem Details body is shaped.

It sits **after** request logging (see above) and **before** routing, so it catches exceptions
thrown by anything downstream — routing, model binding, controller actions, and everything the
`Dispatcher` calls into.

---

## ProblemDetailsFactory

A static helper that builds RFC 9457 Problem Details bodies with one consistent shape, so every
error response — regardless of which exception produced it — looks the same to API consumers
([ProblemDetailsFactory.cs](../src/Ordinis.Api/Common/ProblemDetailsFactory.cs)).

Two entry points:

- `Create(...)` — a plain `ProblemDetails` for single-message errors (404, 409, 500).
- `CreateValidation(...)` — a `ValidationProblemDetails` for 422s, carrying FluentValidation's
  field-keyed error dictionary directly in the response body.

Both attach the current request's correlation ID as a `correlationId` extension field
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

- **Controllers** (`AddControllers()`).
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
