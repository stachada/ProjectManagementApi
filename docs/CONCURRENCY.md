# Optimistic concurrency — ETag / If-Match

This project protects `ProjectTask`, `Project`, and `Board` from lost updates using
HTTP's standard optimistic-concurrency mechanism: a client reads a resource, gets back
an `ETag`, and must echo that value in an `If-Match` header on any request that mutates
the resource. If the server's current state no longer matches, the request fails with
`409 Conflict` instead of silently overwriting a change the client never saw.

This doc explains the full round trip end to end. For where `ConcurrencyTokenMiddleware`
sits in the request pipeline relative to the API's other middleware, see
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md).

---

## Why this exists

Without it, two clients editing the same task hit the classic lost-update problem:

1. Client A `GET`s a task (title: "Fix login bug").
2. Client B `GET`s the same task.
3. Client A `PUT`s a change (title: "Fix login bug — urgent").
4. Client B, still working off its stale copy, `PUT`s its own change (description only) —
   and silently clobbers Client A's title change, because B's request never knew A's
   write happened in between.

`RowVersion` alone doesn't fix this. EF Core's own `.IsConcurrencyToken()` check
(`ProjectTaskConfiguration.cs`, `ProjectConfiguration.cs`, `BoardConfiguration.cs`) only
catches a write race that happens *within a single request's own load-then-save window* —
if a handler loads a row, then saves, and nothing else touched that row in between, EF
sees no conflict at all, even if the client's *original* copy (from a `GET` minutes
earlier) was already stale by the time it submitted its edit. That's exactly the Client
B scenario above: B's own load-then-save window is perfectly race-free from EF's point of
view. Catching it requires comparing against what the *client* last saw, not just what
the *server* saw a moment ago — which is what `If-Match` is for.

---

## The token

Every `ProjectTask`, `Project`, and `Board` carries a `RowVersion` (`byte[]?`, `AggregateRoot`
base class), mapped via `.IsConcurrencyToken()` in each entity's `IEntityTypeConfiguration<T>`
(`ProjectTaskConfiguration`, `ProjectConfiguration`, `BoardConfiguration`). That call only tells
EF Core to include the property's original value in the `WHERE` clause of every generated
`UPDATE`/`DELETE` and to throw `DbUpdateConcurrencyException` if zero rows match — it says
nothing about who produces the value.

Unlike SQL Server's native auto-incrementing `rowversion`/`timestamp` column type, `RowVersion`
here is **app-managed, not database-generated** — deliberately, since PostgreSQL has no
equivalent auto-updating column type and this project runs identically on both providers.
`AppDbContext.SaveChangesAsync` assigns a fresh value to every tracked `Added`/`Modified`
`AggregateRoot` before the underlying `SaveChangesAsync` runs:

```csharp
// AppDbContext.SetConcurrencyTokens()
foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
{
    if (entry.State is EntityState.Added or EntityState.Modified)
        entry.Entity.RowVersion = Guid.CreateVersion7().ToByteArray();
}
```

This doesn't weaken the optimistic-concurrency guarantee at all — EF's `WHERE RowVersion =
@original` check and the resulting `DbUpdateConcurrencyException` on a zero-row match work
identically no matter how the new value was produced. The only properties that actually matter
are that it changes on every write and gets compared in the `WHERE` clause, and both hold here
exactly as they would with a database-generated value. (A related detail: mutating a *child*
entity that lives in its own table, e.g. adding a `ProjectMember` to a `Project`, leaves the
parent aggregate root itself `Unchanged` in EF's eyes unless something walks the graph and marks
it dirty — `MarkAggregateRootsDirtyForChangedChildren`, called from the same
`SetConcurrencyTokens`, does exactly that, which is why `Project`'s `RowVersion` still advances
on a member add/role-change/remove even though none of `Project`'s own scalar properties
changed.)

Each DTO's `ConcurrencyToken` property is that raw `RowVersion`, Base64-encoded so it's
safe to put in a header or JSON body:

```csharp
// TaskMapper.ToDto / ProjectMapper.ToDto / ProjectMapper.ToBoardDto — identical pattern in all three
ConcurrencyToken = task.RowVersion is { Length: > 0 }
    ? Convert.ToBase64String(task.RowVersion)
    : string.Empty,
```

The `{ Length: > 0 }` guard exists for unit tests that construct an entity in memory and
never persist it — `RowVersion` is null until EF Core's first `SaveChanges` sets it, so
the mapper degrades to an empty string rather than throwing `ArgumentNullException` from
`Convert.ToBase64String`.

---

## Step by step

### 1. `GET` returns the current token as an `ETag`

`TasksController.GetById`, `ProjectsController.GetById`, and `BoardsController.GetById`
all do the same thing after fetching the DTO:

```csharp
if (!string.IsNullOrEmpty(dto.ConcurrencyToken))
{
    Response.Headers.ETag = $"\"{dto.ConcurrencyToken}\"";
}
```

The surrounding quotes aren't decorative — [RFC 9110 §8.8.3](https://www.rfc-editor.org/rfc/rfc9110.html#section-8.8.3)
defines an `ETag`'s value as a quoted string (`"<opaque-tag>"`, optionally prefixed
`W/` for a *weak* validator). Every HTTP client and cache that understands `ETag` expects
the quotes; omitting them would still work against this API specifically (since only this
API's own middleware ever parses the value back), but would break interoperability with
any standard HTTP tooling (browsers, `curl -O`/conditional caching, reverse-proxy cache
layers) that treats the header per spec.

### 2. The client stores it and sends it back as `If-Match`

A client that wants to update the resource re-sends the exact `ETag` value, quotes
included, as `If-Match`:

```http
PUT /api/v1/tasks/{id}
If-Match: "AAAAAAAAB9E="
Content-Type: application/json

{ "title": "...", ... }
```

### 3. `ConcurrencyTokenMiddleware` extracts and decodes it

(`src/Ordinis.Api/Common/ConcurrencyTokenMiddleware.cs`) — see
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#concurrencytokenmiddleware) for its place in
the pipeline. On every request, if an `If-Match` header is present:

1. Strip a leading `W/` (weak-validator prefix) and surrounding `"..."` quotes, leaving
   the raw Base64 payload.
2. `Convert.FromBase64String(...)` it to a `byte[]`.
3. Store the result on `HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey]`.

If the header is absent, or present but not valid Base64, the middleware leaves the item
unset — it does **not** reject the request itself. A missing token and a malformed token
end up indistinguishable to everything downstream, which is deliberate: the middleware's
only job is decoding, not deciding which endpoints require a token. That decision is left
entirely to each command's own FluentValidation rule (next step), so the "is this
required here?" policy lives in exactly one place per endpoint — the same place every
other per-command validation rule already lives — instead of being duplicated as
attribute metadata the middleware would also need to understand.

### 4. The controller passes the token into the command

Every guarded action reads the same item and forwards it as the command's `IfMatch`
parameter:

```csharp
byte[]? ifMatch = HttpContext.Items[ConcurrencyTokenMiddleware.ItemsKey] as byte[];
var command = new UpdateTask(..., IfMatch: ifMatch);
```

This is the only place `HttpContext` is involved — `Ordinis.Application` command records
just carry a plain `byte[]?`, with no knowledge of headers, middleware, or ASP.NET Core at
all. `IAppDbContext`/`ConcurrencyGuard` (below) never see an `HttpContext` either.

### 5. FluentValidation requires it — before the handler ever runs

Every guarded command's validator has a rule identical in shape to this one
(`UpdateTaskValidator`, `MoveTaskValidator`, `ArchiveProjectValidator`,
`RenameBoardValidator`, ...):

```csharp
RuleFor(x => x.IfMatch)
    .NotNull()
    .WithMessage("If-Match header is required.");
```

The `Dispatcher` runs all registered validators *before* invoking a command handler
(`Dispatcher.ValidateAsync`, called from `Dispatcher.SendAsync`), so a request with no
`If-Match` never reaches the handler at all — it fails fast with `422 Unprocessable
Entity` and a field error on `IfMatch`, the same `ValidationProblemDetails` shape every
other validation failure in this API uses.

**This has one sharp edge worth knowing:** validation runs before the handler's own
`NotFoundException` check, so a request against a *nonexistent* resource with no
`If-Match` header still gets `422`, not `404` — the validator never gets far enough to
let the handler discover the resource doesn't exist. A request that *does* supply an
`If-Match` (any well-formed value) against a nonexistent resource correctly reaches the
handler and gets `404`, since `ConcurrencyGuard.EnsureMatch` (next step) only runs *after*
the handler's existence check.

### 6. The handler compares it against the current `RowVersion`

Every guarded handler follows the same shape — load the entity, check existence, then
guard, all before applying any mutation:

```csharp
ProjectTask task = await db.Tasks
    .FirstOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken)
        ?? throw new NotFoundException(nameof(ProjectTask), command.TaskId);

ConcurrencyGuard.EnsureMatch(task.RowVersion, command.IfMatch, "Task", command.TaskId);

task.Move(...); // or Rename, Archive, Assign, etc.
```

`ConcurrencyGuard.EnsureMatch` (`src/Ordinis.Application/Common/ConcurrencyGuard.cs`) does
a byte-for-byte comparison (`ReadOnlySpan<byte>.SequenceEqual`) between the entity's
current `RowVersion` and the client-supplied token, and throws `ConcurrencyException` on a
mismatch. This is the **proactive** check — it catches a client working off *any* older
snapshot, including one fetched long before this request, which is the scenario the
`DbUpdateConcurrencyException` catch below can't see.

The existing `try/catch (DbUpdateConcurrencyException)` around `SaveChangesAsync` in every
handler is untouched and still there as **defense in depth** — it catches the narrower
case of a write race between *this handler's own* load and its own save (two requests
both passing the proactive check with the same starting token, then racing each other to
`SaveChangesAsync`). Both paths throw the same `ConcurrencyException`, so the client sees
one consistent failure mode regardless of which check caught the conflict:

```csharp
public ConcurrencyException(string entityType, Guid entityId)          // proactive: ConcurrencyGuard
public ConcurrencyException(string entityType, Guid entityId, Exception inner)  // reactive: DbUpdateConcurrencyException
```

### 7. `GlobalExceptionMiddleware` maps it to `409 Conflict`

Same as every other domain/application exception in this API — see
[API_INFRASTRUCTURE.md](API_INFRASTRUCTURE.md#globalexceptionmiddleware)'s exception table.
No special-casing needed here; `ConcurrencyException` was already a recognized type before
this feature added its second constructor.

---

## What's guarded and what isn't

| Aggregate | Guarded endpoints | Not guarded |
|---|---|---|
| `ProjectTask` | `PUT /tasks/{id}`, `POST /tasks/{id}/move`\|`assign`\|`unassign`\|`close`\|`reopen`, `DELETE /tasks/{id}` | Comments, attachments (see below) |
| `Project` | `PUT /projects/{id}`, `POST /projects/{id}/archive`\|`unarchive`, `DELETE /projects/{id}`, `POST /projects/{id}/members`, `PUT .../members/{userId}/role`, `DELETE .../members/{userId}` | — |
| `Board` | `PUT /boards/{id}/name`, `POST /boards/{id}/archive`\|`unarchive` | — |

**Comment and attachment endpoints are deliberately excluded**, even though they mutate
their owning `ProjectTask` row. `EditCommentHandler`'s own remarks explain why: only the
original author may edit a comment (enforced by `EditCommentValidator`), so a genuine
conflict would require the *same user* to submit two conflicting edits at the same
instant — a scenario narrow enough that last-write-wins is an acceptable, deliberate
trade-off. Adding a `RowVersion` (or requiring `Task`'s own `If-Match`) for that edge case
was judged disproportionate to the risk. Project-member endpoints, by contrast, *are*
guarded despite being "child" operations on `Project`, since any project member can
plausibly race another member's own concurrent edit to the membership list — a much more
realistic multi-user conflict than the single-author comment case.

Create endpoints (`POST /tasks`, `POST /projects`, `POST /projects/{id}/boards`, ...)
are never guarded — there's no prior version to compare against when a resource doesn't
exist yet.

---

## Testing it yourself

```bash
# 1. Fetch the current ETag
curl -i https://localhost:5001/api/v1/tasks/{id}
# ETag: "AAAAAAAAB9E="

# 2. Reuse it on a mutating call — succeeds
curl -i -X POST https://localhost:5001/api/v1/tasks/{id}/move \
  -H 'If-Match: "AAAAAAAAB9E="' \
  -H 'Content-Type: application/json' \
  -d '{ "status": "InProgress", "requestedByUserId": "..." }'
# 204 No Content — and the ETag has now changed underneath you

# 3. Replay the same (now stale) If-Match — fails
curl -i -X POST https://localhost:5001/api/v1/tasks/{id}/move \
  -H 'If-Match: "AAAAAAAAB9E="' \
  -H 'Content-Type: application/json' \
  -d '{ "status": "Done", "requestedByUserId": "..." }'
# 409 Conflict, Problem Details body

# 4. Omit If-Match entirely
curl -i -X POST https://localhost:5001/api/v1/tasks/{id}/move \
  -H 'Content-Type: application/json' \
  -d '{ "status": "Done", "requestedByUserId": "..." }'
# 422 Unprocessable Entity — errors.IfMatch: ["If-Match header is required."]
```

The integration suite (`tests/Ordinis.IntegrationTests/{Tasks,Projects}/...ControllerTests.cs`)
covers all three of these outcomes per controller, plus asserting the `ETag` header value
on `GET` matches the DTO's `ConcurrencyToken` exactly.
