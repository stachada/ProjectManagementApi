# Project Management API — Build Plan

> Use solution name "Ordinis"
> Jira-like REST API · ASP.NET Core · Clean Architecture

---

## Architecture overview

```
src/
├── Ordinis.Domain
│   ├── Tasks/              # Task.cs, TaskStatus.cs, TaskCreated.cs
│   ├── Projects/           # Project.cs, ProjectCreated.cs
│   ├── Organizations/
│   ├── Boards/
│   ├── Comments/
│   └── Users/              # User.cs, Role.cs
│
├── Ordinis.Application
│   ├── Tasks/
│   │   ├── Commands/       # CreateTask.cs, MoveTask.cs, AssignTask.cs ...
│   │   ├── Queries/        # GetTaskById.cs, GetTasksFiltered.cs ...
│   │   ├── Validators/     # CreateTaskValidator.cs ...
│   │   └── Dtos/           # TaskDto.cs
│   ├── Projects/
│   ├── Boards/
│   ├── Comments/
│   ├── Users/
│   └── Common/             # ICommandHandler.cs, IQueryHandler.cs, Dispatcher.cs
│
├── Ordinis.Infrastructure
│   ├── Tasks/              # TaskConfiguration.cs (IEntityTypeConfiguration<Task>)
│   ├── Projects/
│   ├── Boards/
│   ├── Comments/
│   ├── Users/
│   └── Persistence/        # AppDbContext.cs, migrations, DI registration
│
├── Ordinis.Api
│   ├── Tasks/              # TasksController.cs
│   ├── Projects/           # ProjectsController.cs
│   ├── Boards/
│   ├── Comments/
│   ├── Users/
│   ├── MinimalApis/        # Auth.cs, Webhooks.cs (Minimal API endpoint groups)
│   └── Common/             # Middleware, filters, startup extensions
│
tests/
├── Ordinis.UnitTests
├── Ordinis.IntegrationTests
└── Ordinis.Benchmarks   # added in Phase 8
```

---

## Parallel execution map

Some phases have hard dependencies; others can run concurrently once their prerequisites are done.

```
Phase 1 ──► Phase 2 ──┬──► Phase 3 ──┬──► Phase 5 ──► Phase 6
                       │              │
                       └──► Phase 4 ──┘
                                           Phase 7  (can start alongside Phase 5)
                                           Phase 8  (grows incrementally from Phase 3 onward)
                                           Phase 9  (can start alongside Phase 5)
                                           Phase 10 (can start alongside Phase 4)
```

| Phase | Depends on | Can run in parallel with |
|---|---|---|
| 1 — Solution setup | — | — |
| 2 — Domain | 1 | — |
| 3 — Application (CQRS) | 2 | 4 |
| 4 — Infrastructure | 2 | 3 |
| 5 — Core REST endpoints | 3, 4 | 7, 9, 10 |
| 6 — Advanced REST features | 5 | 7, 8 |
| 7 — Security | 3 | 5, 6, 8 |
| 8 — Testing | 3 (grows continuously) | all |
| 9 — Docs & DX | 5 | 6, 7, 10 |
| 10 — CI/CD & Docker | 4 | 5, 6, 7, 9 |

---

## How to work on individual tasks with Claude

Use this prompt pattern in a new message:

> **"Let's work on Phase X — [task description]"**

Examples:
- *"Let's work on Phase 2 — define the Task aggregate root"* → get C# code with invariants, value objects, domain events
- *"Let's work on Phase 4 — configure dual provider DbContext"* → full startup wiring with appsettings switching
- *"Let's work on Phase 6 — implement ETags and If-Match"* → middleware/filter code ready to drop in
- *"Let's work on Phase 7 — JWT + refresh token flow"* → full auth setup with endpoint examples

You can also paste existing code and ask to review, extend, or debug it in the context of a specific task.

---

## Phase 1 — Repository & solution setup

- [x] Create GitHub repo with `.gitignore` (dotnet) and MIT license
- [x] Scaffold solution: `dotnet new sln`
- [x] Add projects: Api, Application, Domain, Infrastructure, UnitTests, IntegrationTests
- [x] Wire project references (Clean Architecture layers)
- [x] Establish feature-folder structure within each project (Tasks/, Projects/, Boards/, Comments/, Users/, Common/)
- [x] Add README with architecture overview and setup instructions
- [x] Configure EditorConfig and .NET code style ruleset
- [x] Initialize ASP.NET Core User Secrets for the Api project (`dotnet user-secrets init`)

---

## Phase 2 — Domain layer

> ⚠️ Required before Phase 3 and Phase 4 can start.

- [ ] Define core entities: `Organization`, `Project`, `Board`, `Task`, `Comment`, `Attachment`, `User`
- [ ] Add `ValueObject` base class (`Domain/Common/ValueObject.cs`) — structural equality infrastructure for future complex value objects (e.g. `EmailAddress`, `TaskTitle`)
- [ ] Add domain enumerations: `TaskStatus` (with `TaskStatusExtensions` state machine), `Priority`, `Role`
- [ ] Add `TaskStatusExtensions` transition map — drives `Task.Move()` invariant guards (Phase 2) and HATEOAS link generation (Phase 6)
- [ ] Define domain events: `TaskCreated`, `TaskMoved`, `TaskAssigned`, `CommentAdded`
- [ ] Add aggregate roots and invariant guards
- [ ] Add soft delete support: `IsDeleted` / `DeletedAt` fields on entities that should not be hard-deleted (`Task`, `Project`, `Board`, `Comment`)
- [ ] Add concurrency tokens (`RowVersion`) to entities subject to concurrent edits (`Task`, `Project`, `Board`)
- [ ] No external dependencies in this layer (enforce in csproj)

---

## Phase 3 — Application layer (CQRS)

> ✅ Can run in parallel with Phase 4 once Phase 2 is done.

- [ ] Install FluentValidation
- [ ] Define `ICommandHandler<TCommand>` and `IQueryHandler<TQuery, TResult>` interfaces
- [ ] Add Commands: `CreateTask`, `UpdateTask`, `DeleteTask`, `MoveTask`, `AssignTask`
- [ ] Add Queries: `GetTaskById`, `GetTasksFiltered`, `GetProjectBoard`
- [ ] Implement handlers for each command and query
- [ ] Add FluentValidation validators for each command
- [ ] Add a dispatcher service to resolve and invoke handlers via DI
- [ ] Inject `AppDbContext` directly into handlers (no repository abstraction)
- [ ] Handle `DbUpdateConcurrencyException` in command handlers and translate to `409 Conflict` with Problem Details
- [ ] Define DTOs and implement manual mapping (static mapper classes or extension methods); consider Mapster if boilerplate grows

---

## Phase 4 — Infrastructure layer

> ✅ Can run in parallel with Phase 3 once Phase 2 is done.
> ✅ Docker / CI setup (Phase 10) can start here.

- [ ] Install EF Core with both `Microsoft.EntityFrameworkCore.SqlServer` and `Npgsql.EntityFrameworkCore.PostgreSQL`
- [ ] Select provider via `appsettings.json` (`DatabaseProvider: "SqlServer"` or `"PostgreSQL"`)
- [ ] Configure `AppDbContext` and register the correct provider at startup based on config
- [ ] Define entity configurations (Fluent API, separate `IEntityTypeConfiguration<T>` classes)
- [ ] Configure `RowVersion` concurrency tokens in entity configurations for `Task`, `Project`, `Board`
- [ ] Configure global EF Core query filters for soft deletes (`HasQueryFilter(e => !e.IsDeleted)`)
- [ ] Add and manage migrations per provider
- [ ] Add Dapper for reporting / complex read queries
- [ ] Configure structured logging (Serilog)
- [ ] Add request/response logging middleware (method, path, status code, duration, correlation ID)
- [ ] Add `X-Correlation-ID` header middleware — generate or propagate per request, attach to logs and responses
- [ ] Add health check endpoint (`/health`)
- [ ] Add background job infrastructure (Hangfire or hosted service)
- [ ] Implement Outbox pattern for reliable domain event dispatch — store events in DB within the same transaction, deliver via background job

---

## Phase 5 — API layer — core REST endpoints

> ⚠️ Requires Phase 3 and Phase 4.
> ✅ Phase 7 (security), Phase 9 (docs), and Phase 10 (CI/CD) can run alongside.

- [ ] Scaffold controllers for resource-heavy endpoints: `Organizations`, `Projects`, `Boards`, `Tasks`, `Comments`, `Users`
- [ ] Scaffold Minimal API endpoints for focused routes: auth (`/auth/login`, `/auth/refresh`), webhooks, search
- [ ] CRUD endpoints for all resources
- [ ] Resource relationship endpoints: `GET /projects/{id}/tasks`, `GET /tasks/{id}/comments`
- [ ] Filtering: `GET /tasks?assignee=123&status=InProgress&priority=High`
- [ ] Sorting: `GET /tasks?sort=-createdAt`
- [ ] Pagination: `GET /tasks?page=1&pageSize=20`
- [ ] Sparse fields: `GET /tasks?fields=id,title,status`
- [ ] Search endpoint: `GET /tasks/search?q=login+bug`

---

## Phase 6 — Advanced REST features

> ⚠️ Requires Phase 5.

- [ ] State transitions: `POST /tasks/{id}/move`, `/assign`, `/close`, `/reopen`
- [ ] HATEOAS: add `_links` to task and project responses
- [ ] Optimistic concurrency with ETags and `If-Match` — tie ETag value to EF Core `RowVersion` for end-to-end consistency
- [ ] Return `409 Conflict` with Problem Details on concurrent update collisions
- [ ] Idempotency keys on POST endpoints to prevent duplicate command execution (`Idempotency-Key` header)
- [ ] API versioning: `/api/v1` and `/api/v2` routes
- [ ] Global exception handling middleware — catch all unhandled exceptions and map to Problem Details (RFC 9457) responses
- [ ] Problem Details responses (RFC 9457) with consistent error shape across all endpoints
- [ ] Rate limiting middleware (ASP.NET Core built-in)
- [ ] Response caching headers
- [ ] Webhooks: `POST /projects/{id}/webhooks`, fire on task events
- [ ] Audit log endpoints: `GET /projects/{id}/audit`

---

## Phase 7 — Security

> ✅ Can start as soon as Phase 3 is done; wire into controllers during Phase 5.

- [ ] JWT authentication (issue + validate tokens)
- [ ] Refresh token flow
- [ ] Role-based authorization (Admin, Member, Viewer)
- [ ] Policy-based authorization (e.g. only project members can view tasks)
- [ ] Secure endpoints with `[Authorize]` and custom policies

---

## Phase 8 — Testing & benchmarking

> ✅ Start unit tests from Phase 3 onward; integration tests from Phase 5 onward. Grows continuously.

- [ ] Unit tests for domain logic and validators (xUnit)
- [ ] Unit tests for command/query handlers
- [ ] Integration tests with `WebApplicationFactory` and test DB
- [ ] API-level tests for all endpoints (happy path + error cases)
- [ ] Concurrency conflict tests — simulate simultaneous edits and assert `409 Conflict` responses
- [ ] Test coverage report
- [ ] Benchmark EF Core vs Dapper for the same read query (e.g. `GetTasksFiltered`) using BenchmarkDotNet — scaffold `Ordinis.Benchmarks` project at this point
- [ ] Benchmark manual mapping vs Mapster across varying collection sizes (1, 100, 10 000 items)
- [ ] Benchmark middleware pipeline overhead — raw endpoint vs full middleware stack
- [ ] Load test concurrent write throughput (`PUT /tasks/{id}`) to validate concurrency handling under pressure (k6 or NBomber)

---

## Phase 9 — Developer experience & docs

> ✅ Can start alongside Phase 5.

- [ ] Configure built-in OpenAPI support (.NET 10) with XML comments and examples
- [ ] Add Scalar UI as the interactive docs frontend
- [ ] Add example HTTP files (`requests.http` / Bruno collection)
- [ ] Update README: architecture diagram, local setup, env vars, Docker

---

## Phase 10 — Cloud-ready & CI/CD

> ✅ Can start alongside Phase 4; finalize after Phase 5.

- [ ] Dockerfile for the API
- [ ] `docker-compose` with API + database
- [ ] GitHub Actions: build, test, lint on every PR
- [ ] GitHub Actions: Docker image publish on main merge
- [ ] Environment-specific `appsettings` (Development, Production)
- [ ] Configure GitHub Actions Secrets for sensitive values (connection strings, JWT signing key) and inject as environment variables in the pipeline
- [ ] Document secrets strategy in README: User Secrets for local dev, GitHub Actions Secrets for CI/CD, environment variables for Docker/production

---

## Key design decisions

| Topic | Choice | Reason |
|---|---|---|
| Architecture | Clean Architecture | Clear separation, testable, recruiter-recognized |
| API style | Controllers + Minimal APIs | Controllers for resource endpoints, Minimal APIs for auth, health, webhooks |
| CQRS | Manual dispatch (no MediatR) | Less indirection, explicit handler resolution via DI |
| Mapping | Manual (static mappers / extensions) | Zero overhead, compiler-safe; Mapster available if scale demands it |
| Validation | FluentValidation | Rich rules, wired manually into command handlers |
| ORM | EF Core (no repository pattern) + Dapper | DbContext injected directly; Dapper for complex reads |
| Database | SQL Server + PostgreSQL (switchable) | Provider selected via config; same migrations strategy |
| Soft deletes | `IsDeleted` / `DeletedAt` + global query filter | Realistic for project management domain; no data loss |
| Domain events | Outbox pattern (DB + background job) | Reliable delivery without distributed transaction |
| Observability | Serilog + correlation IDs + request logging | Full traceability per request across logs |
| Secrets | User Secrets (dev) → GitHub Actions Secrets (CI) → env vars (prod) | Standard .NET approach, no extra dependencies, no secrets in Git |
| Docs | .NET 10 built-in OpenAPI + Scalar UI | No Swashbuckle dependency, modern interactive UI |

---

## Git workflow

| Topic | Choice |
|---|---|
| Strategy | GitHub Flow — feature branches off `main`, merged via PR |
| Branch naming | `feature/phase-N-description`, `fix/description`, `chore/description`, `docs/description` |
| Commit style | Conventional Commits — `type(scope): description` |
| Merge strategy | Squash merge to keep `main` history linear and readable |
| Tagging | Tag `main` at the end of each phase: `v0.N-phaseN-description` |

### Branch naming examples

```
feature/phase-2-domain-entities
feature/phase-3-cqrs-task-commands
feature/phase-4-efcore-dual-provider
fix/task-concurrency-409-response
chore/update-build-plan
docs/readme-architecture-diagram
```

### Conventional Commits examples

```
feat(tasks): add CreateTask command handler with FluentValidation
feat(auth): implement JWT token issuance and refresh flow
fix(concurrency): translate DbUpdateConcurrencyException to 409 Conflict
chore: update Directory.Build.props target framework
docs: add architecture diagram to README
```

### Phase tags

| Tag | Milestone |
|---|---|
| `v0.0-phase1-solution-setup` | Phase 1: Repository & solution setup complete |
| `v0.1-phase1-setup` | Phase 1: Repository & solution setup complete |
| `v0.2-phase2-domain` | Phase 2: Domain layer complete |
| `v0.3-phase3-application` | Phase 3: Application / CQRS layer complete |
| `v0.4-phase4-infrastructure` | Phase 4: Infrastructure layer complete |
| `v0.5-phase5-core-api` | Phase 5: Core REST endpoints complete |
| `v0.6-phase6-advanced-rest` | Phase 6: Advanced REST features complete |
| `v0.7-phase7-security` | Phase 7: Security complete |
| `v0.8-phase8-testing` | Phase 8: Testing & benchmarking complete |
| `v0.9-phase9-docs` | Phase 9: Developer experience & docs complete |
| `v0.10-phase10-cicd` | Phase 10: CI/CD & Docker complete |

---

## Progress tracking

Use the interactive checklist in the build plan chat thread, or check off items directly in this file.
