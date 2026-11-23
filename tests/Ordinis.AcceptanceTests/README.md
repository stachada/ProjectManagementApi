# Ordinis Acceptance Specifications (Gherkin)

Executable behavioural specification of the Ordinis API, written in
[Gherkin](https://cucumber.io/docs/gherkin/reference/) and run with
[Reqnroll](https://reqnroll.net/) (the maintained successor to SpecFlow) on xUnit.
The `.feature` files describe **intended** behaviour in business language.

## Running

```sh
dotnet test tests/Ordinis.AcceptanceTests              # needs Docker (SQL Server Testcontainer)
dotnet test tests/Ordinis.AcceptanceTests --filter "FullyQualifiedName~TaskWorkflow"   # one feature
ORDINIS_RUN_GAPS=1 dotnet test tests/Ordinis.AcceptanceTests   # also run @gap scenarios (expected to fail)
```

Outcomes:

- **Passed / failed**: the scenario is fully bound and ran.
- **Skipped, "known gap"**: tagged `@gap`; skipped unless `ORDINIS_RUN_GAPS=1` (see the gap register).
- **Skipped, undefined steps**: the scenario uses a step with no binding yet. `reqnroll.json` sets
  `missingOrPendingStepsOutcome` to `Ignore`, which xUnit reports as skipped (`Inconclusive` would
  be reported as a *failure* — xUnit 2 has no inconclusive outcome). Binding a feature turns its
  skips into passes or failures — the skip count is the remaining binding backlog.

The suite runs in CI as part of `dotnet test Ordinis.slnx`, alongside the integration tests.

## Binding status

963 scenarios (outline rows expanded): **887 pass, 76 skipped, 0 fail.**

| Feature | Status |
| --- | --- |
| Tasks/task-workflow | ✅ bound — 70 pass |
| Tasks/task-listing | ✅ bound — 43 pass |
| Tasks/task-assignment | ✅ bound — 35 pass |
| Tasks/task-editing | ✅ bound — 34 pass |
| Tasks/task-comments | ✅ bound — 32 pass |
| Tasks/task-creation | ✅ bound — 28 pass |
| Tasks/task-attachments | ✅ bound — 24 pass |
| Tasks/task-hypermedia | ✅ bound — 18 pass |
| Projects/project-membership | ✅ bound — 47 pass |
| Projects/projects | ✅ bound — 46 pass |
| Projects/project-lifecycle | ✅ bound — 36 pass |
| Projects/project-audit | ✅ bound — 21 pass |
| Boards/boards | ✅ bound — 50 pass |
| Users/user-management | ✅ bound — 42 pass |
| Organizations/organizations | ✅ bound — 31 pass |
| Users/user-tasks | ✅ bound — 18 pass |
| Organizations/organization-lifecycle | ✅ bound — 15 pass |
| Users/user-lifecycle | ✅ bound — 15 pass |
| Authorization/role-permissions | ✅ bound — 38 pass |
| Authorization/resource-visibility | ✅ bound — 24 pass |
| Authorization/tenant-isolation | ✅ bound — 22 pass |
| Search/search | ✅ bound — 22 pass |
| Auth/login | ✅ bound — 14 pass |
| Auth/token-refresh | ✅ bound — 12 pass; 4 `@needs-clock` skipped (Tier 3) |
| Auth/logout | ✅ bound — 5 pass |
| Platform/optimistic-concurrency | ✅ bound — 32 pass |
| Platform/idempotency | ✅ bound — 17 pass; 1 `@needs-clock` skipped (Tier 3) |
| Platform/error-responses | ✅ bound — 16 pass |
| Platform/sparse-fieldsets | ✅ bound — 12 pass |
| Platform/api-versioning | ✅ bound — 11 pass |
| Platform/caching | ✅ bound — 11 pass |
| Platform/correlation | ✅ bound — 6 pass |
| Platform/health | ✅ bound — 6 pass |
| Platform/rate-limiting | ✅ bound — 6 pass; 2 `@needs-clock` skipped (Tier 3) |
| Platform/pagination | ✅ bound — 28 pass |
| *all other features* | ⏳ not bound yet (reported as skipped) |

## Host variants and simulated failures

Most scenarios run against one in-process API host. A few need it configured differently;
`TestHost` builds these variants on first use, from the same configuration and database, with
no background jobs (so outbox messages are never processed twice):

| Need | How |
| --- | --- |
| Development / Production environment ("Given the API runs in the … environment") | `TestHost.Development` / `TestHost.Production` (`UseEnvironment`); the scenario switches host through `World.Host` |
| The production rate limiter (`@needs-rate-limiter`) | `TestHost.RateLimited` registers `KeepProductionRateLimiter`, so the limiter is exactly `Program.cs`'s. A test header sets `RemoteIpAddress`, and every scenario gets its own address and new users, so no scenario inherits another's quota |
| "Given the database is unavailable" | `DatabaseOutage`, an EF Core connection interceptor on the main host: while on, opening a connection fails like an unreachable SQL Server (instantly, with connection details in the message, so leaks are detectable). The test's own seeding is exempt. Switched off after every scenario |
| Log lines ("a request log entry records …") | `CapturedLogs`, a Serilog sink registered in DI and picked up by `Program.cs`'s `ReadFrom.Services` — so scenarios check the real logging pipeline. Cleared after every scenario |

## How it works

```text
Features/**/*.feature   ── Reqnroll generates one xUnit test class per feature
Steps/                  ── step definitions, grouped by concern (setup, outcomes, tasks, webhooks)
Support/
├── TestHost            ── run-wide: one API host + SQL Server container + webhook receiver;
│                          database reset (Respawn) after every scenario
├── AcceptanceApiFactory── the shared Ordinis.Testing factory, with a random DB port, 1 s webhook
│                          polling, and a URL guard that also trusts the in-process receiver
├── World               ── per-scenario names → IDs ("Bob", "Fix login bug"), last response,
│                          versions (ETags) personas have read
├── Seeder              ── "Given" state written through the real domain model, not the API
├── ApiDriver           ── the only code that knows routes, headers and JSON; real JWTs per persona
└── WebhookReceiver     ── loopback Kestrel server the background dispatcher really POSTs to
```

- **Givens seed, Whens call the API.** Arrangement goes straight through domain factories and
  methods (so invariants still hold), keeping each scenario fast and independent of the endpoint
  under test. Every "When" is a real HTTP call as a real, signed-in persona.
- **Writes state the current version by default.** Steps add `If-Match` with the task's current
  version unless they say `using the version <persona> read` (stale) or `without stating a version`.
- **Hidden helper users.** Seeded projects are "founded" by a hidden user, removed as soon as a
  named Admin joins, so membership matches the feature text; read-back assertions (e.g. the audit
  trail) use a hidden org-Admin observer so they don't depend on any persona's access.
- **Scenarios run sequentially** (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`)
  because they share one database.

---

## Writing style

- **Declarative, not HTTP-level.** Steps say *what* a persona does
  ("Bob moves the task to Done"), never *how* (`POST /api/v1/tasks/{id}/move`).
  Routes, headers, ETags and status codes live in step bindings.
- One `Feature` per file; business rules grouped with `Rule:`.
- `Background` holds only setup that every scenario in the file needs.
- Matrices (transition tables, role permissions, validation limits) use
  `Scenario Outline` with named `Examples:` blocks.
- Enum values by name (`InReview`, `High`, `Viewer`), never their integers.
- Domain error codes are quoted exactly as the API emits them
  (`urn:ordinis:error:<code>` → `"task.invalid-status-transition"`).

## Personas

| Persona | Who | Use |
| --- | --- | --- |
| **Olivia** | Org Admin of "Acme"; not a project member unless stated | org-wide authority |
| **Alice** | Admin of project "Apollo" | project administration |
| **Bob** | Member of project "Apollo" | everyday contributor |
| **Victor** | Viewer of project "Apollo" | read-only |
| **Nora** | User in "Acme", not a member of "Apollo" | resource hiding (404) |
| **Mallory** | User in a different organization "Globex" | tenant isolation (404) |

Each feature's `Background` states which personas it sets up.

## Step glossary

Use these canonical phrasings so bindings stay few and reusable. `<…>` marks a
parameter.

### Given — arrangement

| Step | Notes |
| --- | --- |
| `an organization "<org>"` / `… whose org Admin is <persona>` | |
| `<persona> is a user in "<org>"` | org role Member, no project membership |
| `an organization "<org>" with a user <persona>` | other tenant |
| `a project "<project>" in "<org>" with a board "<board>"` | |
| `<persona> is a(n) <Role> of project "<project>"` | Viewer / Member / Admin |
| `a task "<title>" on board "<board>"` | status Backlog, unassigned |
| `the task "<title>" has status <Status>` | seeded directly, bypassing the workflow |
| `the task "<title>" is assigned to <persona>` | |
| `<persona> has read the task "<title>"` | captures the current version (ETag) |
| `<persona> has <past-tense action>` | an action performed successfully as arrangement |
| `project "<project>" has a webhook subscribed to "<event>"` | fake receiver |

### When — action

`<persona> <verb>s …` — e.g. `creates a task`, `moves the task "<t>" to <Status>`,
`closes`, `reopens`, `renames … to "<new>"`, `assigns … to <persona>`,
`unassigns`, `comments "<text>" on …`.
Modifiers: `… using the version <persona> read` (stale If-Match),
`… without stating a version` (no If-Match), `an anonymous caller …` (no token).

### Then — outcome

| Step | HTTP |
| --- | --- |
| `the request succeeds` | 2xx |
| `the request is rejected as malformed` | 400 — the request cannot be bound (invalid JSON, a required field absent, an unconvertible value) |
| `the request is rejected as unauthenticated` | 401 |
| `the request is rejected as forbidden` | 403 |
| `the request is rejected as not found` | 404 |
| `the request is rejected as conflicting` | 409 |
| `the request is rejected as invalid` / `… with error "<code>"` | 422 (+ domain error code) — the request binds but breaks a rule (blank, too long, unknown reference, …) |
| `the request is rejected as too many requests` | 429 |
| `the task "<t>" has status <S>` / `still has status <S>` | read-back assertion |

## Tags

| Tag | Meaning |
| --- | --- |
| `@area-<name>` | feature area (tasks, projects, boards, organizations, users, auth, authorization, search, webhooks, platform) |
| `@smoke` | minimal happy-path set for a quick run |
| `@gap` | specifies intended behaviour the API does **not** meet yet — see the gap register. Expected to fail when bound. |
| `@role-matrix` | permission matrix outline |
| `@needs-clock` | requires a controllable `TimeProvider` (token expiry, retry backoff); the integration factory currently uses the real clock |
| `@needs-rate-limiter` | requires the global rate limiter, which `OrdinisApiFactory` disables |
| `@background` | asserts on outbox / webhook background processing (eventual consistency — poll with timeout) |
| `@multi-tenant` | involves a second organization |

## Gap register

Scenarios tagged `@gap` carry a `# GAP-nn` comment pointing here. When a gap is fixed,
its `@gap` tags are removed (the scenarios become ordinary regression specs) and the row
moves to **Fixed gaps** below.

*No open gaps — all 26 found by the specs are fixed (see below).*

### Known edge cases (documented, not fixed)

- **Simultaneous last-admin demotion.** If an organization's only two org Admins demote (or
  deactivate) *each other* within the same instant, both requests can pass
  `OrganizationAdminGuard`'s "another active Admin exists" check, leaving no active Admin. Each
  request changes a *different* user row, so `If-Match` cannot detect it; closing it would need
  an organization-level lock or a serializable transaction around the check. Requires two
  Admins acting within milliseconds of each other, and an operator can restore an Admin
  directly in the database — accepted as a documented edge case.

### Fixed gaps

| Id | Was | Fix |
| --- | --- | --- |
| GAP-01 | Five list queries (`GetTasksFiltered`, `GetProjectsFiltered`, `GetProjectTasks`, `GetBoardTasks`, `GetProjectAudit`) only capped `pageSize` at 100, so 0 / negative sizes reached SQL (`FETCH NEXT 0 ROWS`, negative `Take`) → 500; all seven list queries could also overflow `(page - 1) * pageSize` | One shared `PageRequest.Normalize` (`pageSize` 1..100, `page` ≥ 1 and capped so the offset fits an `int`) used by all seven list queries. Regression specs: `pagination.feature` (bound, 28 scenarios); unit tests `PageRequestTests` |
| GAP-02 | Reopen was a plain move to ToDo, so it also succeeded from Backlog and InProgress (`-> ToDo` is a legal move there) | New domain operation `ProjectTask.Reopen` (only from Done, then delegates to `Move`, still raising `TaskMoved`) behind a `ReopenTask` command; `422` `task.invalid-status-transition` otherwise. Regression tests: `ProjectTaskTests` (Reopen region); `task-workflow.feature` (now fully passing) |
| GAP-03 | `AddProjectMemberValidator` only checked that the user existed, so a user of another organization could be added to a project | The user must belong to the project's organization; a cross-tenant ID gets the same "User not found." as a nonexistent one (no tenant probing). Regression tests: `AddProjectMemberValidatorTests`; `project-membership.feature`, `tenant-isolation.feature` |
| GAP-04 | Assignees (`CreateTask`, `AssignTask`) were only checked for existence — a Viewer, a non-member or another tenant's user could be assigned (authorization only checks the caller) | The assignee must be a project contributor (project Member/Admin, or org Admin of the project's organization), via the shared `ProjectAccessQueries.CanContributeAsync`, also reused for `CreateTask`'s caller check. Regression tests: `AssignTaskValidatorTests`, `CreateTaskValidatorTests`; `task-assignment`, `task-creation`, `tenant-isolation` features |
| GAP-05 | The last org Admin could demote or deactivate themselves, leaving the organization unadministrable | `OrganizationAdminGuard` (called by `ChangeUserOrgRole` and `DeactivateUser`) requires another **active** org Admin first; `422` `organization.last-admin-demotion` / `organization.last-admin-deactivation`. Regression tests: `LastOrgAdminRuleTests`; `user-management`, `user-lifecycle` features |
| GAP-06 | Access tokens were only checked cryptographically, so a deactivated user kept full access — and a demoted org Admin kept Admin authority (the role was read from the token) — until the token expired (up to 15 minutes) | `JwtBearerEvents.OnTokenValidated` calls `ITokenPrincipalRevalidator` on every authenticated request: unknown/inactive user → `401`; otherwise the role claim is replaced with the user's current org role, so demotions and promotions apply immediately. One primary-key lookup per request, deliberately uncached. Regression tests: `Ordinis.IntegrationTests/Auth/TokenRevalidationTests`; `user-lifecycle.feature` |
| GAP-07 | A project could be created in a suspended organization (the validator message already claimed otherwise) | `CreateProjectValidator` requires an active organization. Regression tests: `CreateProjectValidatorTests`; `projects`, `organization-lifecycle` features |
| GAP-08 | Archiving a board or project didn't lock its tasks: only `CreateTask` checked (the board only); every other task, comment and attachment mutation went through | `TaskContainerGuard.EnsureWritableAsync` (one query for both flags) is called by all 11 task command handlers; archived = fully read-only, rejected with `422` `project.archived` / `board.archived`. Reads keep working. Regression tests: `Ordinis.IntegrationTests/Tasks/ArchivedContainerTests` (13 mutations × board/project); GAP-08 scenarios in `task-creation`, `task-editing`, `project-lifecycle`, `boards` |
| GAP-09 | Changing the role of / removing a non-member returned `422` `project.member-not-found` | Handlers check membership first and return `404` (a missing `/members/{userId}` sub-resource); the domain guard stays as a backstop. Regression tests: handler unit tests, `ProjectsControllerTests.*_NotAMember_Returns404`; `project-membership.feature` |
| GAP-10 | Renaming a project/organization skipped the name-uniqueness check | Shared `NameUniquenessQueries`: a name clashes by slug **or** by another resource's current name (ignoring case, since renames don't change slugs), on create and rename; soft-deleted projects count. Regression tests: `UpdateProjectValidatorTests`, `UpdateOrganizationValidatorTests`, `CreateProjectValidatorTests`; `projects`, `organizations` features |
| GAP-11 | Any project Member could remove any attachment | `ProjectTask.RemoveAttachment` requires the uploader or a moderator (project/org Admin), mirroring comment removal: `422` `task.attachment-not-uploader`; the stored file is kept. Moderator check shared via `ProjectAccessQueries.CanModerateAsync`. Regression tests: `ProjectTaskTests`, `RemoveAttachmentHandlerTests`; `task-attachments.feature` |
| GAP-12 | Users and organizations had a `RowVersion` but exposed no `ETag` and ignored `If-Match`, so a read-modify-write lost update was possible | Same contract as every other aggregate: `ETag` on `GET /users/{id}` and `/organizations/{id}`; `If-Match` required on their 7 writes (missing → `422`, stale → `409`) — a pre-1.0 breaking change. Session bookkeeping (refresh-token rotation) no longer rotates a user's version. Regression tests: `UserAndOrganizationConcurrencyTests`, `IfMatchRequiredValidatorTests` |
| GAP-13 | Two concurrent requests with the same `Idempotency-Key` both executed: the middleware looked the key up, and only cached the result *after* running | `IIdempotencyStore.TryReserveAsync` atomically reserves the key before the request runs; an identical request meanwhile gets `409` "Request in progress" + `Retry-After` and is not executed; the reservation is completed on `2xx` or released otherwise. Regression tests: `IdempotencyInFlightTests` (deterministic, via `ConcurrencyRaceInterceptor`), `InMemoryIdempotencyStoreTests` |
| GAP-14 | Task `Status`/`Priority` are stored as strings (`HasConversion<string>()`), so `sortBy=status/priority` ordered alphabetically (priority descending: Medium, Low, High, Critical) | `EntityQueryableExtensions.OrderByEnumValue` sorts by a `CASE` rank of the declared enum order in `GetTasksFiltered`, `GetProjectTasks`, `GetBoardTasks`, `GetUserTasks`; string storage kept. Regression specs: `task-listing.feature` sort scenarios; unit tests `EntityQueryableExtensionsTests` |
| GAP-15 | Re-creating a deleted project's name passed validation (the soft-delete query filter hid it) but violated the unfiltered unique `(OrganizationId, Slug)` index → 500 | `CreateProjectValidator` checks slug uniqueness with `IgnoreQueryFilters()`, so a deleted project's slug stays reserved (422 on `name`). Regression tests: `ProjectsControllerTests.CreateProject_NameOfSoftDeletedProject_Returns422AndCreatesNothing`; `project-lifecycle.feature` "The name of a deleted project stays reserved" |
| GAP-16 | `User.Create` / `Organization.Create` lower-case email and slug, but `Login` compared the input as typed (trim only), so mixed-case sign-in worked on SQL Server's case-insensitive collation and failed on PostgreSQL | `Login` normalizes email and slug with `Trim().ToLowerInvariant()`, matching how they are stored. Regression tests: `Ordinis.UnitTests/Application/Users/Commands/LoginHandlerTests` (InMemory compares case-sensitively, like PostgreSQL) |
| GAP-17 | **Security (SSRF):** the `WebhookDelivery` HttpClient followed redirects (default `AllowAutoRedirect = true`), so a public receiver could redirect delivery to loopback / private / cloud-metadata addresses that `WebhookUrlGuard` never vets | primary handler `SocketsHttpHandler { AllowAutoRedirect = false }`; a 3xx is a failed attempt. Regression test: `Ordinis.IntegrationTests/Common/WebhookDeliveryHttpClientTests` |
| GAP-18 | An attachment did not say who uploaded it, although only the uploader (or a project / org Admin) may remove it — a client could not tell whether to offer "remove" | `AttachmentDto.UploadedByUserId`, mapped in `TaskMapper.ToAttachmentDto`. Found binding `task-attachments.feature`. Regression tests: `TaskMapperTests.ToDto_Attachments_MapAllFields_OrderedByUploadedAt`, `TasksControllerTests.AddAttachment_ValidFile_Returns201WithDownloadUrl`; "Uploading an attachment" scenario |
| GAP-19 | A valid file name over ~218 characters (the validator allows 255) → 500: the stored file is named `{guid}_{name}`, which exceeded the filesystem's 255-character limit per path component | `LocalFileStorageService` shortens the *stored* name to 100 characters, keeping the extension; the original name is kept in the database and returned unchanged. Found binding `task-attachments.feature`. Regression tests: `TasksControllerTests.AddAttachment_FileNameAtMaximumLength_Returns201AndKeepsTheFullName`; "A file name of exactly 255 characters is accepted" scenario |
| GAP-20 | No `JsonStringEnumConverter`: task `status`/`priority` and member `role` were sent as numbers, and JSON bodies with names (`{"status":"InProgress"}`, as the docs showed) were rejected with 400 — while query strings accepted names and `UserDto.OrgRole` was already a string | `JsonStringEnumConverter` on the MVC and Minimal API JSON options, and in `EventJson.Options` for outbox payloads (the audit trail) and webhook envelopes. Names out; names *or* numbers in, so ordinal clients keep working (a pre-1.0 change for clients reading numbers). Found binding `project-membership.feature`. Regression tests: `EnumSerializationTests`, `EventJsonTests`; the acceptance driver now sends names |
| GAP-21 | The organization details had no `slug` — the value users must state when signing in, so a client could not discover it through the API — and no `updatedAt`, unlike every other resource | `OrganizationDto.Slug` and `UpdatedAt`, mapped in `OrganizationMapper`. Found binding `organizations.feature`. Regression tests: `OrganizationMapperTests.ToDto_AllFieldsMapCorrectly`, `OrganizationsControllerTests.GetById_ReturnsSlugAndUpdatedAt`; "Viewing one's own organization" scenario |
| GAP-22 | `GetUserTasks` sent an unknown `sortBy` to newest first whatever `sortDescending` said, while every other task list falls back to `createdAt` in the requested direction | The fallback shares the `createdAt` branch, so it honours the direction. Found binding `user-tasks.feature`. Regression tests: `GetUserTasksHandlerTests.HandleAsync_UnknownSortField_SortsByCreatedAtInTheRequestedDirection`; "An unknown sort field falls back to creation order" scenario |
| GAP-23 | A missing refresh token was reported on field `Token` — the command's property — although `POST /auth/refresh` takes `refreshToken`, so a client could not match the error to its input (the only request field renamed on its way to a command) | `RefreshTokenValidator` reports it as `RefreshToken` (`OverridePropertyName`); the command keeps `Token`, since a record member cannot share the record's name. Found binding `token-refresh.feature`. Regression test: `AuthEndpointsTests.Refresh_MissingToken_Returns422OnTheRequestField`; "The refresh token is required" scenario |
| GAP-24 | FluentValidation translated validation messages into the server's current UI culture, so the API's error language depended on the host (Polish on a Polish machine: "Pole 'Refresh Token' nie może być puste.") while every other message is English | `ValidatorOptions.Global.LanguageManager.Enabled = false` in `AddApplicationServices`: always the built-in English messages. Found binding `token-refresh.feature` on a Polish-locale machine. Regression test: `AuthEndpointsTests.Validation_ServerUiCultureIsNotEnglish_MessagesStayEnglish` (runs the request under `pl-PL`, so it reproduces on any host) |
| GAP-25 | A retried upload with the same `Idempotency-Key` was rejected as "already used with a different request body": the request fingerprint hashed the raw multipart body, whose boundary the client picks anew (usually at random) for every request — so request keys never worked for attachments | `IdempotencyMiddleware` replaces the multipart boundary with a fixed marker before hashing, so the hash depends only on the parts. Found binding `idempotency.feature`. Regression tests: `IdempotencyMiddlewareTests.Post_RetriedUploadWithNewMultipartBoundary_ReplaysAndCreatesOneAttachment` (and `…SameKeyDifferentUploadContent_Returns409Conflict`, which guards that different content still conflicts); "Every create endpoint honours the request key: uploads …" scenario |
| GAP-26 | An authenticated caller over the limit got `429` without `Retry-After` — although the body says "Retry after the interval given in the Retry-After header": `OnRejected` only copied the limiter's retry-after metadata, which the fixed window (anonymous callers) reports and the sliding window (authenticated callers) does not | `OnRejected` always sets `Retry-After`: the metadata when present, otherwise one sliding-window segment (`WindowSeconds / SegmentsPerWindow`, 10 s by default). Found binding `rate-limiting.feature`. Regression test: `RateLimitingTests.SlidingWindowRejection_StillReturnsRetryAfterInSeconds`; "The 501st request by one user within a minute is rejected" scenario |

## Out of scope (by design, not gaps)

Refresh-token reuse detection, webhook payload signing (HMAC), `412`/`428`/`304`
conditional responses (the API deliberately uses `409`/`422`), deprecation /
sunset headers, a webhook delivery-log endpoint, and the Scalar UI (Phase 10).

## Domain guards not reachable through the API

These domain error codes exist as defence-in-depth guards, but an earlier
layer (validator or authorization policy) always rejects the request first, so
the API never emits them. They belong in domain unit tests, not here:

| Code | Shadowed by |
| --- | --- |
| `attachment.invalid-size` | `AddAttachment` validator (`size > 0`) → plain validation error |
| `attachment.not-found` | `RemoveAttachment` handler → 404 before the domain call |
| `comment.update-deleted` | `EditComment` validator ("not found or not the author") |
| `project.member-already-exists` | `AddProjectMember` validator (already a member) |
| `task.deleted` | task-scoped authorization policies → deleted task is 404 |

## Coverage matrix

Every endpoint and background behaviour maps to at least one feature
(paths relative to [`Features/`](Features/)).

| Endpoint / behaviour | Feature(s) |
| --- | --- |
| `GET /api/v1/tasks` | Tasks/task-listing, Platform/pagination, Platform/sparse-fieldsets |
| `GET /api/v1/tasks/{id}` | Tasks/task-hypermedia, Platform/optimistic-concurrency, Platform/caching |
| `GET /api/v1/tasks/{id}/comments` | Tasks/task-comments |
| `GET /api/v1/tasks/{id}/attachments` | Tasks/task-attachments |
| `POST /api/v1/tasks` | Tasks/task-creation, Platform/idempotency |
| `PUT /api/v1/tasks/{id}` | Tasks/task-editing |
| `DELETE /api/v1/tasks/{id}` | Tasks/task-editing |
| `POST /api/v1/tasks/{id}/move` | Tasks/task-workflow |
| `POST /api/v1/tasks/{id}/assign` | Tasks/task-assignment |
| `POST /api/v1/tasks/{id}/unassign` | Tasks/task-assignment |
| `POST /api/v1/tasks/{id}/close` | Tasks/task-workflow |
| `POST /api/v1/tasks/{id}/reopen` | Tasks/task-workflow |
| `POST /api/v1/tasks/{id}/comments` | Tasks/task-comments, Platform/idempotency |
| `PUT /api/v1/tasks/{id}/comments/{commentId}` | Tasks/task-comments |
| `DELETE /api/v1/tasks/{id}/comments/{commentId}` | Tasks/task-comments |
| `POST /api/v1/tasks/{id}/attachments` | Tasks/task-attachments, Platform/idempotency |
| `DELETE /api/v1/tasks/{id}/attachments/{attachmentId}` | Tasks/task-attachments |
| `GET /api/v2/tasks/{id}` | Tasks/task-hypermedia, Platform/api-versioning |
| `GET /api/v1/projects` | Projects/projects, Platform/pagination |
| `GET /api/v1/projects/{id}` | Projects/projects, Platform/caching |
| `GET /api/v1/projects/{id}/tasks` | Tasks/task-listing |
| `GET /api/v1/projects/{id}/members` | Projects/project-membership |
| `GET /api/v1/projects/{id}/boards` | Projects/projects |
| `GET /api/v1/projects/{id}/audit` | Projects/project-audit |
| `POST /api/v1/projects` | Projects/projects, Platform/idempotency |
| `PUT /api/v1/projects/{id}` | Projects/projects |
| `DELETE /api/v1/projects/{id}` | Projects/project-lifecycle |
| `POST /api/v1/projects/{id}/archive` | Projects/project-lifecycle |
| `POST /api/v1/projects/{id}/unarchive` | Projects/project-lifecycle |
| `POST /api/v1/projects/{id}/members` | Projects/project-membership, Platform/idempotency |
| `PUT /api/v1/projects/{id}/members/{userId}/role` | Projects/project-membership |
| `DELETE /api/v1/projects/{id}/members/{userId}` | Projects/project-membership |
| `GET /api/v1/boards/{id}` | Boards/boards, Platform/caching |
| `GET /api/v1/boards/{id}/tasks` | Tasks/task-listing |
| `POST /api/v1/projects/{projectId}/boards` | Boards/boards, Platform/idempotency |
| `PUT /api/v1/boards/{id}/name` | Boards/boards |
| `POST /api/v1/boards/{id}/archive` | Boards/boards |
| `POST /api/v1/boards/{id}/unarchive` | Boards/boards |
| `GET /api/v1/users/{id}` | Users/user-management, Platform/caching |
| `GET /api/v1/users/{id}/tasks` | Users/user-tasks |
| `POST /api/v1/users` | Users/user-management, Platform/idempotency |
| `PUT /api/v1/users/{id}` | Users/user-management |
| `PUT /api/v1/users/{id}/org-role` | Users/user-management |
| `POST /api/v1/users/{id}/deactivate` | Users/user-lifecycle |
| `POST /api/v1/users/{id}/reactivate` | Users/user-lifecycle |
| `GET /api/v1/organizations/{id}` | Organizations/organizations, Platform/caching |
| `GET /api/v1/organizations/{id}/projects` | Organizations/organizations |
| `POST /api/v1/organizations` | Organizations/organizations, Platform/idempotency |
| `PUT /api/v1/organizations/{id}` | Organizations/organizations |
| `POST /api/v1/organizations/{id}/suspend` | Organizations/organization-lifecycle |
| `POST /api/v1/organizations/{id}/reactivate` | Organizations/organization-lifecycle |
| `GET /api/v1/search` | Search/search |
| `POST /api/v1/projects/{id}/webhooks` | Webhooks/webhook-subscriptions, Platform/idempotency |
| `DELETE /api/v1/projects/{id}/webhooks/{webhookId}` | Webhooks/webhook-subscriptions |
| `GET /api/v1/projects/{id}/webhooks` | Webhooks/webhook-subscriptions |
| `POST /auth/login` | Auth/login |
| `POST /auth/refresh` | Auth/token-refresh |
| `POST /auth/logout` | Auth/logout |
| `GET /health` | Platform/health |
| `GET /openapi/v1.json` (Development) | Platform/health |
| Outbox → webhook fan-out → delivery, retries, SSRF re-check | Webhooks/webhook-delivery |
| Authorization policies, 404 resource hiding | Authorization/resource-visibility, Authorization/role-permissions |
| Tenant isolation | Authorization/tenant-isolation |
| Problem Details / error codes | Platform/error-responses |
| Rate limiting | Platform/rate-limiting |
| Correlation id + request logging | Platform/correlation |
