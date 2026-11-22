# Ordinis Acceptance Specifications (Gherkin)

Executable-to-be behavioural specification of the Ordinis API, written in
[Gherkin](https://cucumber.io/docs/gherkin/reference/). The `.feature` files
describe **intended** behaviour in business language. Step bindings
([Reqnroll](https://reqnroll.net/), the maintained successor to SpecFlow) will be
added in a later task; until then this folder has no `.csproj` and is not part of
`Ordinis.slnx`, so nothing generates "pending" test failures.

When bindings are added they should reuse the existing integration-test
infrastructure in [`tests/Ordinis.IntegrationTests/Infrastructure/`](../Ordinis.IntegrationTests/Infrastructure/):
`OrdinisApiFactory` (SQL Server Testcontainer + Respawn reset),
`IntegrationTestBase` seeding/auth helpers (`CreateAccessTokenAsync`,
`SendAsync` with `If-Match`, `AssertValidationProblemAsync`).

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
| `the request is rejected as malformed` | 400 |
| `the request is rejected as unauthenticated` | 401 |
| `the request is rejected as forbidden` | 403 |
| `the request is rejected as not found` | 404 |
| `the request is rejected as conflicting` | 409 |
| `the request is rejected as invalid` / `… with error "<code>"` | 422 (+ domain error code) |
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

Scenarios tagged `@gap` carry a `# GAP-nn` comment pointing here.

| Id | Current behaviour | Intended behaviour |
| --- | --- | --- |
| GAP-01 | `pageSize` < 1 is passed through (empty page / EF error) in `GetTasksFiltered`, `GetProjectsFiltered`, `GetProjectTasks`, `GetBoardTasks`, `GetProjectAudit` | clamped to 1–100 like `GetUserTasks` |
| GAP-02 | Reopen is a plain move to ToDo, so it also succeeds from InProgress | reopen only from Done |
| GAP-03 | A user from another organization can be added to a project | rejected as invalid |
| GAP-04 | An assignee need not be a project member | assignee must be a Member or Admin of the project |
| GAP-05 | The last org Admin can demote or deactivate themselves | rejected as invalid |
| GAP-06 | A deactivated user's access token keeps working until expiry | rejected immediately |
| GAP-07 | A project can be created in a suspended organization | rejected as invalid |
| GAP-08 | Tasks on an archived board/project can still be edited, moved, assigned | rejected as invalid |
| GAP-09 | Changing the role of / removing a non-member returns 422 `project.member-not-found` | 404 not found |
| GAP-10 | Renaming a project/organization skips the name-uniqueness check | rejected as invalid on clash |
| GAP-11 | Any project Member can remove any attachment | only the uploader, a project Admin or the org Admin |
| GAP-12 | Users and organizations expose no version (ETag) and accept writes without If-Match | same optimistic concurrency as other aggregates |
| GAP-13 | Two concurrent requests with the same Idempotency-Key both execute | only one executes; the other replays or is rejected as conflicting |
| GAP-14 | Task `Status`/`Priority` are stored as strings (`ProjectTaskConfiguration` `HasConversion<string>()`), so `sortBy=status/priority` orders alphabetically | ordered by workflow position / severity |
| GAP-15 | Re-creating a deleted project's name passes validation (soft-delete filter hides it) but violates the unfiltered unique `(OrganizationId, Slug)` index → 500 | rejected as invalid on `name` (slug stays reserved) |
| GAP-16 | `User.Create` lower-cases emails but `Login` compares the input as typed, so mixed-case sign-in works on SQL Server (CI collation) but fails on PostgreSQL | email matched case-insensitively on both providers |
| GAP-17 | **Security (SSRF):** the `WebhookDelivery` HttpClient follows redirects (default `AllowAutoRedirect = true`), so a public receiver can redirect delivery to loopback / private / cloud-metadata addresses that `WebhookUrlGuard` never vets | redirects are not followed; a 3xx counts as a failed attempt |

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
