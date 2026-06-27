# Project Management API — Build Plan

> Solution name: **Ordinis**
> Jira-like REST API · ASP.NET Core · Clean Architecture · Portfolio project targeting senior .NET developers

---

## Architecture overview

```
src/
├── Ordinis.Domain
│   ├── Common/             # Entity.cs, AuditableEntity.cs, AggregateRoot.cs, ValueObject.cs, IDomainEvent.cs
│   ├── Tasks/              # ProjectTask.cs, ProjectTaskStatus.cs, Priority.cs
│   │                       # Comment.cs, Attachment.cs
│   │                       # TaskCreated.cs, TaskMoved.cs, TaskAssigned.cs, TaskUnassigned.cs
│   │                       # CommentAdded.cs, CommentRemoved.cs, AttachmentAdded.cs, AttachmentRemoved.cs
│   ├── Projects/           # Project.cs, Board.cs, ProjectMember.cs, Role.cs
│   ├── Organizations/      # Organization.cs
│   └── Users/              # User.cs
│
├── Ordinis.Application
│   ├── Common/             # ICommandHandler.cs, IQueryHandler.cs, IDispatcher.cs, Dispatcher.cs
│   │                       # ValidationException.cs, ConcurrencyException.cs
│   │                       # ApplicationServiceExtensions.cs, ApplicationAssemblyMarker.cs
│   ├── Tasks/
│   │   ├── Commands/       # CreateTask.cs, UpdateTask.cs, DeleteTask.cs, MoveTask.cs
│   │   │                   # AssignTask.cs, UnassignTask.cs
│   │   │                   # AddComment.cs, EditComment.cs, RemoveComment.cs
│   │   │                   # AddAttachment.cs, RemoveAttachment.cs
│   │   ├── Queries/        # GetTaskById.cs, GetTasksFiltered.cs
│   │   ├── Validators/     # CreateTaskValidator.cs, UpdateTaskValidator.cs, MoveTaskValidator.cs
│   │   │                   # AssignTaskValidator.cs, AddCommentValidator.cs, EditCommentValidator.cs
│   │   │                   # AddAttachmentValidator.cs
│   │   └── Dtos/           # TaskDto.cs, TaskSummaryDto.cs, CommentDto.cs, AttachmentDto.cs, TaskMapper.cs
│   ├── Projects/
│   │   ├── Commands/       # CreateProject.cs, UpdateProject.cs, DeleteProject.cs
│   │   │                   # AddProjectMember.cs, RemoveProjectMember.cs
│   │   │                   # CreateBoard.cs, ArchiveBoard.cs, RenameBoard.cs
│   │   ├── Queries/        # GetProjectById.cs, GetProjectsFiltered.cs
│   │   │                   # GetProjectTasks.cs, GetProjectMembers.cs
│   │   │                   # GetBoardById.cs, GetBoardTasks.cs
│   │   ├── Validators/     # CreateProjectValidator.cs, UpdateProjectValidator.cs
│   │   │                   # AddProjectMemberValidator.cs, CreateBoardValidator.cs, RenameBoardValidator.cs
│   │   └── Dtos/           # ProjectDto.cs, ProjectSummaryDto.cs, ProjectMemberDto.cs
│   │                       # BoardDto.cs, BoardSummaryDto.cs, ProjectMapper.cs
│   ├── Organizations/
│   │   ├── Commands/       # CreateOrganization.cs, UpdateOrganization.cs
│   │   ├── Queries/        # GetOrganizationById.cs, GetOrganizationProjects.cs
│   │   ├── Validators/     # CreateOrganizationValidator.cs, UpdateOrganizationValidator.cs
│   │   └── Dtos/           # OrganizationDto.cs, OrganizationMapper.cs
│   └── Users/
│       ├── Commands/       # CreateUser.cs, UpdateUser.cs
│       ├── Queries/        # GetUserById.cs, GetUserTasks.cs
│       ├── Validators/     # CreateUserValidator.cs, UpdateUserValidator.cs
│       └── Dtos/           # UserDto.cs, UserMapper.cs
│
├── Ordinis.Infrastructure
│   ├── Common/             # InfrastructureServiceExtensions.cs
│   ├── Tasks/              # ProjectTaskConfiguration.cs, CommentConfiguration.cs, AttachmentConfiguration.cs
│   ├── Projects/           # ProjectConfiguration.cs, BoardConfiguration.cs, ProjectMemberConfiguration.cs
│   ├── Organizations/      # OrganizationConfiguration.cs
│   ├── Users/              # UserConfiguration.cs
│   └── Persistence/        # AppDbContext.cs, OutboxMessage.cs, OutboxMessageConfiguration.cs
│                           # OutboxDispatcherJob.cs, Migrations/
│
├── Ordinis.Api
│   ├── Common/             # GlobalExceptionMiddleware.cs, CorrelationIdMiddleware.cs
│   │                       # ProblemDetailsFactory.cs, ApiServiceExtensions.cs
│   ├── Tasks/              # TasksController.cs
│   ├── Projects/           # ProjectsController.cs, BoardsController.cs
│   ├── Organizations/      # OrganizationsController.cs
│   ├── Users/              # UsersController.cs
│   └── MinimalApis/        # AuthEndpoints.cs, SearchEndpoints.cs, WebhookEndpoints.cs
│
tests/
├── Ordinis.UnitTests
│   ├── Domain/             # Aggregate, value object, state machine tests (Phase 2 — complete)
│   ├── Application/        # Validator and handler unit tests (Phase 9)
│   └── Common/             # Shared test infrastructure
├── Ordinis.IntegrationTests
│   ├── Tasks/              # API-level tests per resource (Phase 9)
│   ├── Projects/
│   └── Common/             # WebApplicationFactory setup, test DB helpers
└── Ordinis.Benchmarks      # EF Core vs Dapper, mapping, middleware (Phase 9)
```

---

## Dependency map

```
Phase 1 ──► Phase 2 ──► Phase 3 ──┬──► Phase 4 ──┬──► Phase 6 ──► Phase 7
                                    │              │
                                    └──► Phase 5 ──┘
                                                        Phase 8   (starts after Phase 4)
                                                        Phase 9   (starts after Phase 6; grows continuously)
                                                        Phase 10  (starts after Phase 6)
                                                        Phase 11  (starts after Phase 5)
                                                        Phase 12  (starts after Phase 10)
```

| Phase | Name | Depends on | Can run in parallel with |
|---|---|---|---|
| 1 | Solution setup | — | — |
| 2 | Domain layer | 1 | — |
| 3 | Application layer — infrastructure | 2 | — |
| 4 | Application layer — features | 3 | 5 |
| 5 | Infrastructure layer | 3 | 4 |
| 6 | API layer — core endpoints | 4, 5 | 8, 11 |
| 7 | API layer — advanced REST | 6 | 9, 10 |
| 8 | Security | 4 | 6, 7, 9 |
| 9 | Testing & benchmarking | 4 (grows continuously) | all |
| 10 | Developer experience & docs | 6 | 7, 8, 9, 11, 12 |
| 11 | CI/CD & Docker | 5 | 6, 7, 8, 9, 10, 12 |
| 12 | Polish & portfolio hardening | 10, 11 | — |

---

## How to work on individual phases with Claude

Use this prompt pattern to start a new session:

> **"Let's work on Phase X — [task description]"**

Examples:
- *"Let's work on Phase 4 — Task commands and validators"*
- *"Let's work on Phase 5 — AppDbContext dual-provider setup"*
- *"Let's work on Phase 6 — TasksController CRUD endpoints"*
- *"Let's work on Phase 7 — ETags and If-Match"*
- *"Let's work on Phase 8 — JWT and refresh token flow"*

Each session: read `BUILD_PLAN.md` first, confirm prerequisites, surface design decisions before writing code.

---

## Phase 1 — Repository & solution setup ✅

- [x] Create GitHub repo with `.gitignore` (dotnet) and MIT license
- [x] Scaffold solution: `dotnet new sln`
- [x] Add projects: `Ordinis.Api`, `Ordinis.Application`, `Ordinis.Domain`, `Ordinis.Infrastructure`, `Ordinis.UnitTests`, `Ordinis.IntegrationTests`
- [x] Wire project references (Clean Architecture layers)
- [x] Establish feature-folder structure within each project
- [x] Add README with architecture overview and setup instructions
- [x] Configure `.editorconfig` and .NET code style ruleset (`Directory.Build.props`)
- [x] Initialize ASP.NET Core User Secrets for `Ordinis.Api` (`dotnet user-secrets init`)

**Git tag:** `v0.1-phase1-solution-setup`

---

## Phase 2 — Domain layer ✅

> No external dependencies in this layer. `Ordinis.Domain.csproj` has zero `PackageReference`s.

- [x] Add base classes: `Entity`, `AuditableEntity`, `AggregateRoot`, `ValueObject`, `IDomainEvent` (`Domain/Common/`)
- [x] Add `InternalsVisibleTo` assembly attributes for `Ordinis.Infrastructure`, `Ordinis.UnitTests`, `Ordinis.IntegrationTests`
- [x] Define aggregate roots with invariant guards: `Organization`, `Project`, `Board`, `ProjectTask`, `User`
- [x] Define supporting entities: `Comment` (inherits `AuditableEntity`), `Attachment` (inherits `Entity`), `ProjectMember`
- [x] Add domain enumerations: `ProjectTaskStatus` (renamed from `TaskStatus` — avoids collision), `Priority`, `Role`
- [x] Add `ProjectTaskStatusExtensions` state machine — adjacency list of valid transitions; consumed by `ProjectTask.Move()` and later by HATEOAS link generation (Phase 7)
- [x] Define domain events as `sealed record`: `TaskCreated`, `TaskMoved`, `TaskAssigned`, `TaskUnassigned`, `CommentAdded`, `CommentRemoved`, `AttachmentAdded`, `AttachmentRemoved`
- [x] Domain methods accept `DateTimeOffset now` as an explicit parameter — `Ordinis.Domain` never calls `DateTimeOffset.UtcNow` or references `TimeProvider`
- [x] Soft delete: `IsDeleted` / `DeletedAt` on `ProjectTask`, `Project`, `Board`, `Comment`
- [x] Concurrency tokens: `RowVersion` (byte array) on `ProjectTask`, `Project`, `Board`
- [x] Primary keys: `Guid.CreateVersion7()` (UUIDv7 — time-ordered, no index fragmentation)

**Key decisions locked:**
- Flat base class hierarchy (`Entity → AuditableEntity → AggregateRoot`) — no interface noise
- `internal` constructors on child entities (`Comment`, `Attachment`, `ProjectMember`) — aggregate roots own their children's lifecycle. `Board` was later promoted to an independent aggregate root with a public `Create` factory (Step 2 refactor)
- `internal` visibility on `ClearDomainEvents()` — only `Ordinis.Infrastructure` may clear events after Outbox dispatch
- `CreatedAt` / `UpdatedAt` — `internal set`, populated by `AppDbContext.SaveChanges` via injected `TimeProvider`
- `DeletedAt`, `JoinedAt`, `UploadedAt` — `private set`, set explicitly via domain method parameters

**Git tag:** `v0.2-phase2-domain`

---

## Phase 3 — Application layer: infrastructure ✅

> Provides the CQRS skeleton. No feature handlers yet — those are Phase 4.
> `Ordinis.Application` references `FluentValidation` and `Microsoft.Extensions.DependencyInjection.Abstractions` only.

- [x] Install `FluentValidation` (v11) and `Microsoft.Extensions.DependencyInjection.Abstractions`
- [x] Define handler interfaces:
  - `ICommandHandler<TCommand>` — void commands (delete, move, assign)
  - `ICommandHandler<TCommand, TResult>` — typed-result commands (create → returns new ID / DTO)
  - `IQueryHandler<TQuery, TResult>` — all queries
- [x] Define `IDispatcher` interface — public contract for controllers
- [x] Implement `Dispatcher` (`internal sealed`) — resolves handlers from `IServiceProvider`; resolves and runs `IValidator<T>` before invoking command handlers (queries are not validated); throws `ValidationException` on failure
- [x] Define `ValidationException` (custom, in `Ordinis.Application`) — decouples the API layer from a direct FluentValidation dependency
- [x] Define `ConcurrencyException` — thrown by command handlers catching `DbUpdateConcurrencyException`; decouples the API layer from EF Core
- [x] Add `ApplicationAssemblyMarker` — anchors `AddValidatorsFromAssemblyContaining<T>()` assembly scanning
- [x] Add `ApplicationServiceExtensions` — `AddApplicationServices(this IServiceCollection)` registers `IDispatcher`, all validators (via assembly scan), and calls per-feature handler registration methods added in Phase 4

**Key decisions locked:**
- Dispatcher owns validation pipeline — handlers receive already-validated commands
- Queries are not validated in the dispatcher — handler throws `ArgumentException` on bad params → `400 Bad Request`
- `ValidationException` is Ordinis-owned — `Ordinis.Api` never references `FluentValidation.ValidationException` directly
- `ConcurrencyException` is Ordinis-owned — `Ordinis.Api` never references `DbUpdateConcurrencyException` directly

**Git tag:** `v0.3-phase3-app-infrastructure`

---

## Phase 4 — Application layer: features

> ⚠️ Requires Phase 3.
> ✅ Can run in parallel with Phase 5 (Infrastructure).
>
> Full CQRS feature implementation for every entity.
> Work order: Tasks → Projects & Boards → Organizations → Users.
> Tasks are done first — they set the pattern. Each subsequent entity follows the same shape.

### Step 1 — Tasks (commands, validators, queries)

**DTOs**
- [x] `TaskSummaryDto` — lean list view (no nested collections)
- [x] `TaskDto` — full detail view (embedded `CommentDto`, `AttachmentDto`)
- [x] `CommentDto`
- [x] `AttachmentDto`
- [x] `TaskMapper` — static extension methods; `ToSummaryDto()`, `ToDto()`, private helpers for comments and attachments

**Commands**
- [x] `CreateTask` + `CreateTaskHandler` + `CreateTaskValidator`
  - Returns `Guid` (new task ID)
  - Handler injects `AppDbContext`, `TimeProvider`; resolves `now` once; calls `ProjectTask.Create(..., now)`
  - Validator: `BoardId` required and exists, `Title` non-empty max 200 chars, `Priority` valid enum value
- [x] `UpdateTask` + `UpdateTaskHandler` + `UpdateTaskValidator`
  - Updates `Title`, `Description`, `Priority`, `DueDate`
  - Catches `DbUpdateConcurrencyException` → throws `ConcurrencyException`
  - Validator: same field rules as create
- [x] `DeleteTask` + `DeleteTaskHandler`
  - Soft delete via `task.SoftDelete(now)`
  - No validator needed (ID-only command)
- [x] `MoveTask` + `MoveTaskHandler` + `MoveTaskValidator`
  - Calls `task.Move(newStatus, userId, now)`
  - Domain enforces valid transition via `ProjectTaskStatusExtensions`
  - Validator: `NewStatus` is a valid enum value
- [x] `AssignTask` + `AssignTaskHandler` + `AssignTaskValidator`
  - Calls `task.Assign(assigneeId, userId, now)`
  - Validator: `AssigneeId` required and exists (user is a project member)
- [x] `UnassignTask` + `UnassignTaskHandler`
  - Calls `task.Unassign(userId, now)`
- [x] `AddComment` + `AddCommentHandler` + `AddCommentValidator`
  - Calls `task.AddComment(authorId, content, now)`
  - Returns `Guid` (new comment ID)
  - Validator: `Content` non-empty, max 10 000 chars
- [x] `EditComment` + `EditCommentHandler` + `EditCommentValidator`
  - Validator: same content rules; author must own the comment
- [x] `RemoveComment` + `RemoveCommentHandler`
  - Calls `task.RemoveComment(commentId, now)`
- [x] `AddAttachment` + `AddAttachmentHandler` + `AddAttachmentValidator`
  - Calls `task.AddAttachment(fileName, contentType, sizeBytes, downloadUrl, now)`
  - Returns `Guid` (new attachment ID)
  - Validator: `FileName` non-empty, `SizeBytes` > 0, `ContentType` non-empty
- [x] `RemoveAttachment` + `RemoveAttachmentHandler`
  - Calls `task.RemoveAttachment(attachmentId)`

**Queries**
- [x] `GetTaskById` + `GetTaskByIdHandler`
  - Loads task with comments and attachments (explicit `.Include()`)
  - Resolves assignee name and comment author names via a single User lookup
  - Returns `TaskDto`; throws `NotFoundException` if not found
- [x] `GetTasksFiltered` + `GetTasksFilteredHandler`
  - Filter params: `BoardId?`, `AssigneeId?`, `Status?`, `Priority?`, `DueBefore?`, `DueAfter?` (via `TaskFilter`)
  - Pagination: `Page`, `PageSize` (max 100)
  - Sorting: `SortBy` field name, `SortDescending` flag
  - Returns `PagedResult<TaskSummaryDto>`

**DI registration**
- [x] `AddTaskHandlers(this IServiceCollection)` — registers all Task command and query handlers as `Scoped`; called from `AddApplicationServices()`

---

### Step 2 — Projects & Boards

**DTOs**
- [ ] `ProjectSummaryDto` — list view (id, name, status, member count, task count, created)
- [ ] `ProjectDto` — detail view (includes `BoardSummaryDto[]`, `ProjectMemberDto[]`)
- [ ] `ProjectMemberDto` — id, userId, userName, role, joinedAt
- [ ] `BoardSummaryDto` — id, name, isArchived, taskCount
- [ ] `BoardDto` — detail view (includes `TaskSummaryDto[]`)
- [ ] `ProjectMapper` — static extension methods

**Commands**
- [ ] `CreateProject` + `CreateProjectHandler` + `CreateProjectValidator`
  - Returns `Guid`
  - Validator: `OrganizationId` required and exists, `Name` non-empty max 100 chars
- [ ] `UpdateProject` + `UpdateProjectHandler` + `UpdateProjectValidator`
  - Updates `Name`, `Description`
  - Catches concurrency exception → `ConcurrencyException`
- [ ] `DeleteProject` + `DeleteProjectHandler`
  - Soft delete via `project.SoftDelete(now)`
- [ ] `AddProjectMember` + `AddProjectMemberHandler` + `AddProjectMemberValidator`
  - Calls `project.AddMember(userId, role, now)`
  - Validator: `UserId` exists, `Role` valid enum value, user not already a member
- [ ] `RemoveProjectMember` + `RemoveProjectMemberHandler`
  - Calls `project.RemoveMember(userId)`
- [ ] `CreateBoard` + `CreateBoardHandler` + `CreateBoardValidator`
  - Creates the board directly via `Board.Create(projectId, name, createdByUserId)` — `Board` is an independent aggregate root
  - Returns `Guid`
  - Validator: `Name` non-empty max 100 chars; project exists and not archived; no duplicate name in project
- [ ] `ArchiveBoard` + `ArchiveBoardHandler`
  - Loads and archives the board directly via `BoardId` only (`board.Archive()`) — no `ProjectId` needed
- [ ] `RenameBoard` + `RenameBoardHandler` + `RenameBoardValidator`
  - Loads and renames the board directly via `BoardId` only (`board.Rename(name)`)
  - Validator: `Name` non-empty max 100 chars; no duplicate name in project

**Queries**
- [ ] `GetProjectById` + `GetProjectByIdHandler` — returns `ProjectDto` with boards and members; throws `NotFoundException`
- [ ] `GetProjectsFiltered` + `GetProjectsFilteredHandler`
  - Filter: `OrganizationId?`, `MemberId?`
  - Pagination + sorting
  - Returns `PagedResult<ProjectSummaryDto>`
- [ ] `GetProjectTasks` + `GetProjectTasksHandler` — all tasks across all boards in a project; same filter/sort/page params as `GetTasksFiltered`
- [ ] `GetProjectMembers` + `GetProjectMembersHandler` — returns `ProjectMemberDto[]`
- [ ] `GetBoardById` + `GetBoardByIdHandler` — returns `BoardDto` with tasks
- [ ] `GetBoardTasks` + `GetBoardTasksHandler` — tasks for a specific board; same filter/sort/page params

**DI registration**
- [ ] `AddProjectHandlers(this IServiceCollection)`

---

### Step 3 — Organizations

**DTOs**
- [ ] `OrganizationDto` — id, name, createdAt, projectCount
- [ ] `OrganizationMapper`

**Commands**
- [ ] `CreateOrganization` + `CreateOrganizationHandler` + `CreateOrganizationValidator`
  - Returns `Guid`
  - Validator: `Name` non-empty max 100 chars
- [ ] `UpdateOrganization` + `UpdateOrganizationHandler` + `UpdateOrganizationValidator`
  - Updates `Name`
  - Validator: same

**Queries**
- [ ] `GetOrganizationById` + `GetOrganizationByIdHandler` — returns `OrganizationDto`; throws `NotFoundException`
- [ ] `GetOrganizationProjects` + `GetOrganizationProjectsHandler` — returns `PagedResult<ProjectSummaryDto>`

**DI registration**
- [ ] `AddOrganizationHandlers(this IServiceCollection)`

---

### Step 4 — Users

**DTOs**
- [ ] `UserDto` — id, displayName, email, organizationId, createdAt
- [ ] `UserMapper`

**Commands**
- [ ] `CreateUser` + `CreateUserHandler` + `CreateUserValidator`
  - Returns `Guid`
  - Validator: `Email` valid format and unique, `DisplayName` non-empty max 100 chars, `OrganizationId` exists
- [ ] `UpdateUser` + `UpdateUserHandler` + `UpdateUserValidator`
  - Updates `DisplayName`
  - Validator: `DisplayName` non-empty max 100 chars

**Queries**
- [ ] `GetUserById` + `GetUserByIdHandler` — returns `UserDto`; throws `NotFoundException`
- [ ] `GetUserTasks` + `GetUserTasksHandler` — tasks assigned to a user; same filter/sort/page params as `GetTasksFiltered`

**DI registration**
- [ ] `AddUserHandlers(this IServiceCollection)`

---

### Step 5 — Shared application infrastructure

- [x] Add `NotFoundException` to `Ordinis.Application/Common/` — thrown by query handlers; global middleware maps to `404 Not Found` with Problem Details
- [x] Add `PagedResult<T>` to `Ordinis.Application/Common/` — wraps list query results with `Items`, `TotalCount`, `Page`, `PageSize`
- [x] Add `TaskFilter` parameter record to `Tasks/Queries/` — keep query objects slim, separate filter concerns from query dispatch (`GetTasksFiltered(TaskFilter? Filter, ...)`)
- [ ] Add `ProjectFilter` parameter record to `Projects/Queries/` (Step 2 — not started)
- [ ] Finalize `AddApplicationServices()` — call all per-feature `AddXxxHandlers()` methods (only `AddTaskHandlers()` wired so far; revisit once Steps 2–4 land)

**Git tag:** `v0.4-phase4-app-features`

---

## Phase 5 — Infrastructure layer

> ⚠️ Requires Phase 3.
> ✅ Can run in parallel with Phase 4.
> ✅ Phase 11 (CI/CD & Docker) can start here.

- [ ] Install packages: `Microsoft.EntityFrameworkCore.SqlServer`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Tools`, `Dapper`, `Hangfire` (or use `IHostedService` for Outbox dispatcher)
- [ ] Configure `AppDbContext`:
  - Constructor injects `TimeProvider` — sets `CreatedAt` / `UpdatedAt` in `SaveChangesAsync` override
  - `DbSet<>` for all aggregate roots: `Organizations`, `Projects`, `Boards`, `Tasks`, `Users`, `OutboxMessages`
  - Provider selected at startup via `appsettings.json` (`DatabaseProvider: "SqlServer"` | `"PostgreSQL"`)
- [ ] Define `IEntityTypeConfiguration<T>` classes — one per entity, in feature folders:
  - `OrganizationConfiguration` — PK, `Name` max length, `IsActive` default
  - `ProjectConfiguration` — PK, FK to `Organization`, `RowVersion`, soft delete filter, `Name` max length
  - `BoardConfiguration` — PK, FK to `Project`, `RowVersion`, `IsArchived` default, `Name` max length
  - `ProjectTaskConfiguration` — PK, FK to `Board`, `RowVersion`, soft delete filter, `Status`/`Priority` stored as `varchar` via `.HasConversion<string>()`
  - `CommentConfiguration` — PK, FK to `ProjectTask`, soft delete filter, `Content` max length
  - `AttachmentConfiguration` — PK, FK to `ProjectTask`, `FileName`/`ContentType`/`DownloadUrl` max lengths
  - `ProjectMemberConfiguration` — composite PK (`ProjectId`, `UserId`), FK to `Project`, FK to `User`, `Role` stored as `varchar`
  - `UserConfiguration` — PK, FK to `Organization`, `Email` unique index, `DisplayName` max length
  - `OutboxMessageConfiguration` — PK, `OccurredAt`, `Type`, `Payload` (`nvarchar(max)` / `text`), `ProcessedAt?`
- [ ] Define `OutboxMessage` entity in `Persistence/`:
  - `Id` (Guid, UUIDv7), `OccurredAt`, `Type` (event type name), `Payload` (JSON), `ProcessedAt?`
- [ ] Add Outbox dispatch to `AppDbContext.SaveChangesAsync`:
  - Intercept `AggregateRoot` instances with pending domain events
  - Serialize each event to `OutboxMessage` and insert in same transaction
  - Call `aggregate.ClearDomainEvents()` after insert
- [ ] Add `OutboxDispatcherJob` — background service that polls `OutboxMessages` where `ProcessedAt` is null, deserializes and dispatches events, marks processed
- [ ] Configure global EF Core query filters for soft deletes on `Project`, `Board`, `ProjectTask`, `Comment`
- [ ] Add and manage migrations per provider — maintain separate migration folders for SQL Server and PostgreSQL
- [ ] Add Dapper — used in query handlers for complex read queries (e.g. `GetTasksFiltered` with joins); inject `IDbConnection` from `AppDbContext.Database.GetDbConnection()`
- [ ] Configure Serilog — structured logging, output to console (dev) and rolling file (prod); enrich with `CorrelationId`, `MachineName`
- [ ] Add `CorrelationIdMiddleware` — generates or propagates `X-Correlation-ID` per request; attaches to `ILogger` scope and response headers
- [ ] Add request/response logging middleware — logs method, path, status code, duration, correlation ID at `Information` level
- [ ] Add health check endpoint (`/health`) — checks DB connectivity
- [ ] Add `InfrastructureServiceExtensions` — `AddInfrastructureServices(this IServiceCollection, IConfiguration)` called from `Program.cs`; registers `AppDbContext`, `TimeProvider.System` as singleton, Serilog, health checks, background job host

**Git tag:** `v0.5-phase5-infrastructure`

---

## Phase 6 — API layer: core endpoints

> ⚠️ Requires Phase 4 and Phase 5.
> ✅ Phase 9 (Testing), Phase 10 (Docs), Phase 11 (CI/CD) can run alongside.

### Shared API infrastructure (do first)
- [ ] Add `GlobalExceptionMiddleware` — catches `ValidationException` → `422`, `ConcurrencyException` → `409`, `NotFoundException` → `404`, unhandled → `500`; all responses use Problem Details (RFC 9457)
- [ ] Add `ProblemDetailsFactory` helper — builds consistent `ProblemDetails` objects across all error cases
- [ ] Add `CorrelationId` to all Problem Details responses via middleware
- [ ] Register middleware in `Program.cs` in correct order: correlation ID → request logging → global exception → routing → auth (Phase 8) → endpoints
- [ ] Add `ApiServiceExtensions` — `AddApiServices(this IServiceCollection)` wires controllers, rate limiting, response caching, CORS

### Controllers (one file per resource)
- [ ] `OrganizationsController`
  - `GET    /api/v1/organizations/{id}` → `GetOrganizationById`
  - `GET    /api/v1/organizations/{id}/projects` → `GetOrganizationProjects` (paged)
  - `POST   /api/v1/organizations` → `CreateOrganization` → `201 Created` with `Location` header
  - `PUT    /api/v1/organizations/{id}` → `UpdateOrganization` → `204 No Content`
- [ ] `ProjectsController`
  - `GET    /api/v1/projects` → `GetProjectsFiltered` (paged, filterable, sortable)
  - `GET    /api/v1/projects/{id}` → `GetProjectById`
  - `GET    /api/v1/projects/{id}/tasks` → `GetProjectTasks` (paged)
  - `GET    /api/v1/projects/{id}/members` → `GetProjectMembers`
  - `GET    /api/v1/projects/{id}/boards` → list boards (from `ProjectDto`)
  - `POST   /api/v1/projects` → `CreateProject` → `201 Created`
  - `PUT    /api/v1/projects/{id}` → `UpdateProject` → `204 No Content`
  - `DELETE /api/v1/projects/{id}` → `DeleteProject` → `204 No Content`
  - `POST   /api/v1/projects/{id}/members` → `AddProjectMember` → `201 Created`
  - `DELETE /api/v1/projects/{id}/members/{userId}` → `RemoveProjectMember` → `204 No Content`
- [ ] `BoardsController`
  - `GET    /api/v1/boards/{id}` → `GetBoardById`
  - `GET    /api/v1/boards/{id}/tasks` → `GetBoardTasks` (paged)
  - `POST   /api/v1/projects/{id}/boards` → `CreateBoard` → `201 Created`
  - `PUT    /api/v1/boards/{id}/name` → `RenameBoard` → `204 No Content`
  - `POST   /api/v1/boards/{id}/archive` → `ArchiveBoard` → `204 No Content`
- [ ] `TasksController`
  - `GET    /api/v1/tasks` → `GetTasksFiltered` (paged, filterable by assignee/status/priority/board, sortable)
  - `GET    /api/v1/tasks/{id}` → `GetTaskById`
  - `GET    /api/v1/tasks/{id}/comments` → comments from `TaskDto` (no separate query needed)
  - `GET    /api/v1/tasks/{id}/attachments` → attachments from `TaskDto`
  - `POST   /api/v1/tasks` → `CreateTask` → `201 Created`
  - `PUT    /api/v1/tasks/{id}` → `UpdateTask` → `204 No Content`
  - `DELETE /api/v1/tasks/{id}` → `DeleteTask` → `204 No Content`
  - `POST   /api/v1/tasks/{id}/comments` → `AddComment` → `201 Created`
  - `PUT    /api/v1/tasks/{id}/comments/{commentId}` → `EditComment` → `204 No Content`
  - `DELETE /api/v1/tasks/{id}/comments/{commentId}` → `RemoveComment` → `204 No Content`
  - `POST   /api/v1/tasks/{id}/attachments` → `AddAttachment` → `201 Created`
  - `DELETE /api/v1/tasks/{id}/attachments/{attachmentId}` → `RemoveAttachment` → `204 No Content`
- [ ] `UsersController`
  - `GET    /api/v1/users/{id}` → `GetUserById`
  - `GET    /api/v1/users/{id}/tasks` → `GetUserTasks` (paged)
  - `POST   /api/v1/users` → `CreateUser` → `201 Created`
  - `PUT    /api/v1/users/{id}` → `UpdateUser` → `204 No Content`

### Minimal API endpoints
- [ ] `SearchEndpoints` (`/api/v1/search?q=&type=tasks|projects`) — delegates to `GetTasksFiltered` / `GetProjectsFiltered` with text search param
- [ ] Auth endpoints scaffolded as placeholder (`/auth/login`, `/auth/refresh`) — fully implemented in Phase 8

### Cross-cutting concerns on all endpoints
- [ ] All list endpoints: filtering, sorting, pagination via query string; return `X-Total-Count` header
- [ ] All list endpoints: sparse fields support via `?fields=` query string; mapper respects field list
- [ ] All endpoints return Problem Details on error (enforced by `GlobalExceptionMiddleware`)
- [ ] All `POST` endpoints: `201 Created` with `Location: /api/v1/{resource}/{id}` header
- [ ] All `PUT` / `DELETE` endpoints: `204 No Content` on success
- [ ] XML doc comments on all controller actions — used by OpenAPI in Phase 10

**Git tag:** `v0.6-phase6-api-core`

---

## Phase 7 — API layer: advanced REST features

> ⚠️ Requires Phase 6.

### State transitions
- [ ] `POST /api/v1/tasks/{id}/move` → `MoveTask` — body: `{ "status": "InProgress" }`
- [ ] `POST /api/v1/tasks/{id}/assign` → `AssignTask` — body: `{ "assigneeId": "..." }`
- [ ] `POST /api/v1/tasks/{id}/unassign` → `UnassignTask`
- [ ] `POST /api/v1/tasks/{id}/close` → `MoveTask` with `status: Closed` (convenience alias)
- [ ] `POST /api/v1/tasks/{id}/reopen` → `MoveTask` with `status: ToDo`

### HATEOAS
- [ ] Add `HateoasLinks` record — `{ rel, href, method }` list embedded in responses as `_links`
- [ ] `TaskDto` gets `_links`: `self`, `move`, `assign`, `delete`, and valid next-status transitions (driven by `ProjectTaskStatusExtensions.GetValidTransitions()`)
- [ ] `ProjectDto` gets `_links`: `self`, `tasks`, `boards`, `members`, `delete`

### Optimistic concurrency
- [ ] `TaskDto.ConcurrencyToken` (already defined in Phase 4) returned as `ETag` response header on `GET /tasks/{id}`
- [ ] `PUT /tasks/{id}` and state transition endpoints require `If-Match` header — middleware extracts and passes to handler
- [ ] Handler compares incoming token to current `RowVersion`; mismatch → `ConcurrencyException` → `409 Conflict`
- [ ] `ConcurrencyTokenMiddleware` — reads `If-Match`, decodes Base64 → `byte[]`, attaches to `HttpContext.Items` for handlers to consume

### Idempotency
- [ ] `IdempotencyMiddleware` — reads `Idempotency-Key` header on `POST` requests; caches response by key (in-memory, 24h TTL); replays cached response on duplicate key
- [ ] Applied to: `POST /tasks`, `POST /tasks/{id}/comments`, `POST /tasks/{id}/attachments`, `POST /projects`, `POST /projects/{id}/members`, `POST /projects/{id}/boards`, `POST /organizations`, `POST /users`

### API versioning
- [ ] Add URL-segment versioning — `/api/v1/` prefix on all existing routes
- [ ] Add `/api/v2/tasks` as a demonstration endpoint — returns `TaskDto` with an additional `_links` field (v2 difference) to show versioning in practice

### Rate limiting
- [ ] Configure ASP.NET Core built-in rate limiting middleware
- [ ] Fixed window policy: 100 requests / 60 seconds per IP (unauthenticated)
- [ ] Sliding window policy: 500 requests / 60 seconds per authenticated user
- [ ] `429 Too Many Requests` response with `Retry-After` header

### Response caching
- [ ] `Cache-Control` headers on read-only GET endpoints (e.g. `GET /tasks/{id}` → `max-age=30`)
- [ ] `Vary: Accept-Encoding, Authorization` on cached responses

### Webhooks
- [ ] `POST /api/v1/projects/{id}/webhooks` → register a webhook URL for a project
- [ ] `DELETE /api/v1/projects/{id}/webhooks/{webhookId}` → unregister
- [ ] `WebhookEndpoints` Minimal API — lightweight registration; no controller
- [ ] `WebhookDispatcherService` — subscribes to `OutboxMessage` events; fires HTTP POST to registered URLs on `TaskCreated`, `TaskMoved`, `TaskAssigned`, `CommentAdded`
- [ ] Webhook payload: standard envelope `{ "event": "task.moved", "occurredAt": "...", "data": { ... } }`
- [ ] Delivery: fire-and-forget with basic retry (3 attempts, exponential backoff); failures logged

### Audit log
- [ ] `GET /api/v1/projects/{id}/audit` → paginated list of domain events for all tasks in the project
- [ ] Backed by `OutboxMessages` table — query by project ID via task/board FK join; no separate audit store needed at this stage
- [ ] `AuditEntryDto` — `{ id, eventType, occurredAt, actorId, payload }`

**Git tag:** `v0.7-phase7-api-advanced`

---

## Phase 8 — Security

> ⚠️ Requires Phase 4.
> ✅ Can run in parallel with Phase 6 and 7; wire `[Authorize]` into controllers during Phase 6.

- [ ] Install `Microsoft.AspNetCore.Authentication.JwtBearer`
- [ ] Implement `POST /auth/login` (Minimal API) — validates credentials, issues JWT access token + refresh token; stores refresh token hash in `User` entity
- [ ] Implement `POST /auth/refresh` (Minimal API) — validates refresh token, issues new access token + rotated refresh token
- [ ] Implement `POST /auth/logout` (Minimal API) — revokes refresh token
- [ ] JWT configuration: issuer, audience, signing key from `appsettings` / User Secrets; token expiry 15 min (access), 7 days (refresh)
- [ ] Add `RefreshToken` field to `User` entity (hashed); add `RefreshTokenExpiresAt` — update `UserConfiguration` accordingly
- [ ] Role-based authorization: `Admin`, `Member`, `Viewer` roles encoded in JWT claims
- [ ] Policy-based authorization:
  - `ProjectMemberPolicy` — user must be a member of the project to access its resources
  - `ProjectAdminPolicy` — user must be `Admin` role or project owner to delete/archive
  - `TaskOwnerPolicy` — user must be assignee or project member to edit task
- [ ] Apply `[Authorize]` to all controllers; apply specific policies per action
- [ ] `AuthorizationHandlers` — custom `IAuthorizationHandler` implementations for each policy; inject `AppDbContext` to check membership
- [ ] Public endpoints (no auth): `GET /health`, `POST /auth/login`, `POST /auth/refresh`

**Git tag:** `v0.8-phase8-security`

---

## Phase 9 — Testing & benchmarking

> ✅ Unit tests can start as soon as Phase 4 is done.
> ✅ Integration tests can start as soon as Phase 6 is done.
> Grows continuously — add tests as each feature is built, don't batch them all at the end.

### Unit tests (Ordinis.UnitTests)
- [x] Domain logic — aggregate invariants, state machine, value object equality (done in Phase 2 session)
- [ ] FluentValidation validators — test each validator in isolation; use `TestValidate()` from FluentValidation.TestHelper
- [ ] Command handler logic — use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`; mock `AppDbContext` via in-memory provider or test doubles
- [ ] `Dispatcher` — verify validation pipeline fires before handler; verify `ValidationException` is thrown on failure

### Integration tests (Ordinis.IntegrationTests)
- [ ] `WebApplicationFactory<Program>` setup with test `appsettings.json` pointing to SQLite or a real test DB
- [ ] Shared `DatabaseFixture` — creates schema, seeds baseline data, resets between tests
- [ ] API-level tests per controller (happy path + common error cases):
  - Tasks: create, get, update, delete, move, assign, add comment, add attachment
  - Projects: create, get, update, delete, add member, create board
  - Organizations: create, get, update
  - Users: create, get, update
- [ ] Concurrency conflict tests — load same entity in two contexts, update both, assert second `PUT` returns `409 Conflict`
- [ ] Validation error tests — submit invalid payloads, assert `422 Unprocessable Entity` with correct error fields
- [ ] Auth tests — unauthenticated requests to protected endpoints return `401`; wrong role returns `403`
- [ ] Rate limiting tests — exceed limit, assert `429 Too Many Requests` with `Retry-After` header
- [ ] Idempotency tests — repeat `POST` with same `Idempotency-Key`, assert same response and no duplicate record

### Benchmarks (Ordinis.Benchmarks)
- [ ] Scaffold `Ordinis.Benchmarks` project with BenchmarkDotNet
- [ ] EF Core vs Dapper: benchmark `GetTasksFiltered` with 10 000 task rows — measure p50/p99 query time
- [ ] Manual mapping vs Mapster: benchmark `TaskMapper.ToDto()` across 1 / 100 / 10 000 items
- [ ] Middleware pipeline overhead: benchmark raw endpoint response vs full middleware stack (correlation ID + request logging + exception handling)
- [ ] Load test: k6 or NBomber script for `PUT /tasks/{id}` concurrent write throughput — validate `409 Conflict` handling under load; target 50 concurrent users

**Git tag:** `v0.9-phase9-testing`

---

## Phase 10 — Developer experience & docs

> ✅ Can start alongside Phase 6.

- [ ] Configure .NET 10 built-in OpenAPI — enable XML doc generation in `.csproj`; add `AddOpenApi()` to DI; annotate controllers with `[ProducesResponseType]` and XML `<summary>` / `<param>` / `<returns>` comments
- [ ] Add Scalar UI — `app.MapScalarApiReference()` at `/scalar`; configure title, theme
- [ ] Document authentication in OpenAPI — add `SecurityScheme` for Bearer JWT; annotate secured endpoints
- [ ] Add `requests.http` file — one example request per endpoint covering happy path; compatible with VS Code REST Client and JetBrains HTTP Client
- [ ] Update README:
  - Architecture diagram (Mermaid)
  - Full local setup steps (clone → user secrets → run)
  - Environment variable reference table
  - Docker quick-start (`docker-compose up`)
  - Link to Scalar UI and `requests.http`
- [ ] Add `CONTRIBUTING.md` — branch naming, commit conventions, PR checklist; targets portfolio reviewers who may fork

**Git tag:** `v0.10-phase10-docs`

---

## Phase 11 — CI/CD & Docker

> ✅ Can start alongside Phase 5.

- [ ] `Dockerfile` — multi-stage build (sdk → publish → runtime); non-root user; `EXPOSE 8080`
- [ ] `docker-compose.yml` — services: `api` + `db` (SQL Server or PostgreSQL selectable); volume for DB data; health check on `api`
- [ ] `docker-compose.override.yml` — local dev overrides (e.g. mount source for hot reload)
- [ ] GitHub Actions — `ci.yml`:
  - Trigger: `push` to any branch, `pull_request` to `main`
  - Steps: checkout → setup .NET 10 → restore → build → test → lint (via `dotnet format --verify-no-changes`)
  - Test results uploaded as artifact
- [ ] GitHub Actions — `publish.yml`:
  - Trigger: `push` to `main` (after squash merge)
  - Steps: build Docker image → push to GitHub Container Registry (`ghcr.io`)
  - Tagged with git SHA and `latest`
- [ ] Environment-specific `appsettings`:
  - `appsettings.json` — defaults, no secrets
  - `appsettings.Development.json` — verbose logging, CORS allow-all
  - `appsettings.Production.json` — minimal logging, strict CORS
- [ ] GitHub Actions Secrets for CI: `CONNECTION_STRING`, `JWT_SIGNING_KEY`; injected as environment variables into the test and publish steps
- [ ] Document secrets strategy in README: User Secrets (local) → GitHub Actions Secrets (CI) → environment variables (Docker/production)

**Git tag:** `v0.11-phase11-cicd`

---

## Phase 12 — Polish & portfolio hardening

> ⚠️ Requires Phase 10 and Phase 11.
> Final pass before treating the project as showcase-ready.

- [ ] Review all XML doc comments — ensure every public controller action, DTO property, and interface method is documented
- [ ] Review OpenAPI spec in Scalar UI — verify all endpoints, request/response schemas, and error responses appear correctly
- [ ] Review `PHASE2_DECISIONS.md` and `FUTURE_IDEAS.md` — ensure README links to them; add a `ARCHITECTURE.md` if decisions warrant a dedicated doc
- [ ] Verify all `BUILD_PLAN.md` items are checked off
- [ ] Final `dotnet format` pass — zero lint warnings
- [ ] Final `dotnet test` pass — zero failures, coverage report generated
- [ ] Run BenchmarkDotNet suite — capture baseline numbers; add results summary to README
- [ ] Run k6/NBomber load test — capture results; add to README
- [ ] Tag `main` as `v1.0-complete`
- [ ] GitHub repo housekeeping: pin repo, add topics (`dotnet`, `csharp`, `rest-api`, `clean-architecture`, `cqrs`, `ddd`), write a compelling repo description targeting .NET hiring managers

**Git tag:** `v1.0-complete`

---

## Key design decisions (locked)

| Topic | Decision | Reason |
|---|---|---|
| Architecture | Clean Architecture — Domain / Application / Infrastructure / Api | Clear separation; each layer has one job; recruiter-recognized |
| Layer organization | Feature-folder (vertical slice) within each layer | Related code is co-located; easy to navigate by domain concept |
| API style | Controllers for all resource endpoints; Minimal APIs for auth, search, webhooks | Controllers suit resource-heavy CRUD + relationships; Minimal APIs suit focused, non-resource routes |
| CQRS | Manual dispatch — `ICommandHandler` / `IQueryHandler` + `IDispatcher`; no MediatR | Explicit DI resolution; no hidden pipeline magic; shows understanding of the pattern without a framework crutch |
| Mapping | Manual static extension methods; Mapster only if boilerplate becomes excessive | Zero overhead; compiler-safe; no reflection |
| Validation | FluentValidation; invoked centrally in `Dispatcher` before handler; `ValidationException` is Ordinis-owned | Single enforcement point; API layer decoupled from FluentValidation |
| ORM | EF Core injected directly into handlers; Dapper for complex reads | No leaky repository abstraction; Dapper for read performance |
| Database | SQL Server + PostgreSQL; provider via `appsettings.json` | Shows provider-agnostic EF Core config; real dual-DB setup |
| Primary keys | `Guid.CreateVersion7()` (UUIDv7) | Sequential, time-ordered; no clustered index fragmentation; client-side generation works with Outbox |
| Time | `TimeProvider` in `AppDbContext` and Application handlers; `DateTimeOffset now` passed explicitly into domain methods | Domain is free of infrastructure concerns; tests use `FakeTimeProvider` |
| Soft deletes | `IsDeleted` / `DeletedAt` + global EF Core query filter | Realistic for PM domain; no data loss; filtered transparently |
| Concurrency | `RowVersion` + ETag + `If-Match` | End-to-end optimistic concurrency; `409 Conflict` on collision |
| Domain events | Outbox pattern — serialize to `OutboxMessages` in same transaction; background job dispatches | Reliable delivery without distributed transactions |
| Exception handling | Custom exception types (`ValidationException`, `ConcurrencyException`, `NotFoundException`) mapped to Problem Details by global middleware | API layer decoupled from EF Core and FluentValidation internals |
| Observability | Serilog + `X-Correlation-ID` + request/response middleware | Full per-request traceability |
| Auth | JWT (15 min) + refresh tokens (7 days); role + policy based | Industry standard; covers both coarse-grained (role) and fine-grained (policy) authorization |
| Docs | .NET 10 built-in OpenAPI + Scalar UI | No Swashbuckle dependency; modern interactive UI |
| Secrets | User Secrets (dev) → GitHub Actions Secrets (CI) → env vars (prod) | Standard .NET approach; nothing in Git |

**Hard constraints — never suggest:**
- ❌ MediatR
- ❌ AutoMapper
- ❌ Swashbuckle / NSwag
- ❌ Repository pattern or unit of work wrapper over EF Core
- ❌ `DateTimeOffset.UtcNow` or `DateTime.UtcNow` anywhere in Domain or Infrastructure

---

## Git workflow

| Topic | Decision |
|---|---|
| Strategy | GitHub Flow — feature branches off `main`, squash-merged via PR |
| Branch naming | `feature/phase-N-description`, `fix/description`, `chore/description`, `docs/description` |
| Commit style | Conventional Commits — `type(scope): description` |
| Merge strategy | Squash merge — keeps `main` history linear and readable for portfolio reviewers |
| Tagging | Tag `main` at end of each phase (see phase tags below) |

### Branch naming examples

```
feature/phase-4-task-commands
feature/phase-5-efcore-dual-provider
feature/phase-6-tasks-controller
feature/phase-7-etags-if-match
fix/task-concurrency-409-response
chore/update-build-plan
docs/readme-architecture-diagram
```

### Conventional Commits examples

```
feat(tasks): add CreateTask command handler with FluentValidation
feat(projects): add GetProjectsFiltered query with pagination
feat(auth): implement JWT token issuance and refresh flow
fix(concurrency): translate DbUpdateConcurrencyException to 409 Conflict
chore: update Directory.Build.props target framework
docs: add architecture diagram to README
test(tasks): add CreateTask validator unit tests
```

### Phase tags

| Tag | Milestone |
|---|---|
| `v0.1-phase1-solution-setup` | Phase 1: Repository & solution setup |
| `v0.2-phase2-domain` | Phase 2: Domain layer |
| `v0.3-phase3-app-infrastructure` | Phase 3: Application layer — CQRS infrastructure |
| `v0.4-phase4-app-features` | Phase 4: Application layer — all commands, queries, DTOs |
| `v0.5-phase5-infrastructure` | Phase 5: Infrastructure layer |
| `v0.6-phase6-api-core` | Phase 6: API layer — core endpoints |
| `v0.7-phase7-api-advanced` | Phase 7: API layer — advanced REST features |
| `v0.8-phase8-security` | Phase 8: Security |
| `v0.9-phase9-testing` | Phase 9: Testing & benchmarking |
| `v0.10-phase10-docs` | Phase 10: Developer experience & docs |
| `v0.11-phase11-cicd` | Phase 11: CI/CD & Docker |
| `v1.0-complete` | Phase 12: Polish & portfolio hardening |

---

## Progress tracking

Check off items in this file as each task is completed.
Each phase session should start by reading this file and confirming prerequisites.
