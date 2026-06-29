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
│   │                       # IFileStorageService.cs
│   │                       # ApplicationServiceExtensions.cs, ApplicationAssemblyMarker.cs
│   ├── Tasks/               # (validators co-located in the same file as their command/query —
│   │   │                    #  e.g. CreateTaskValidator lives inside CreateTask.cs, not a separate Validators/ folder)
│   │   ├── Commands/       # CreateTask.cs, UpdateTask.cs, DeleteTask.cs, MoveTask.cs
│   │   │                   # AssignTask.cs, UnassignTask.cs
│   │   │                   # AddComment.cs, EditComment.cs, RemoveComment.cs
│   │   │                   # AddAttachment.cs, RemoveAttachment.cs
│   │   ├── Queries/        # GetTaskById.cs, GetTasksFiltered.cs
│   │   └── Dtos/           # TaskDto.cs, TaskSummaryDto.cs, CommentDto.cs, AttachmentDto.cs, TaskMapper.cs
│   ├── Projects/
│   │   ├── Commands/       # CreateProject.cs, UpdateProject.cs, DeleteProject.cs
│   │   │                   # AddProjectMember.cs, RemoveProjectMember.cs
│   │   │                   # CreateBoard.cs, ArchiveBoard.cs, RenameBoard.cs
│   │   ├── Queries/        # GetProjectById.cs, GetProjectsFiltered.cs
│   │   │                   # GetProjectTasks.cs, GetProjectMembers.cs
│   │   │                   # GetBoardById.cs, GetBoardTasks.cs
│   │   └── Dtos/           # ProjectDto.cs, ProjectSummaryDto.cs, ProjectMemberDto.cs
│   │                       # BoardDto.cs, BoardSummaryDto.cs, ProjectMapper.cs
│   ├── Organizations/
│   │   ├── Commands/       # CreateOrganization.cs, UpdateOrganization.cs
│   │   ├── Queries/        # GetOrganizationById.cs, GetOrganizationProjects.cs
│   │   └── Dtos/           # OrganizationDto.cs, OrganizationMapper.cs
│   └── Users/
│       ├── Commands/       # CreateUser.cs, UpdateUser.cs
│       ├── Queries/        # GetUserById.cs, GetUserTasks.cs
│       └── Dtos/           # UserDto.cs, UserMapper.cs
│
├── Ordinis.Infrastructure
│   ├── Common/             # InfrastructureServiceExtensions.cs
│   ├── FileStorage/        # LocalFileStorageService.cs, LocalStorageOptions.cs
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

**Git tag:** `v0.0-phase1-solution-setup`

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
- [x] Add `[assembly: InternalsVisibleTo]` (`AssemblyInfo.cs`) for `Ordinis.UnitTests` and `Ordinis.IntegrationTests`
  - **Added later:** introduced in the attachment-storage-handlers work (alongside #21/#22) so Phase 9
    tests can construct `internal` command handlers (e.g. `AddAttachmentHandler`) directly — mirrors
    the equivalent Domain-layer attribute from Phase 2.

**Key decisions locked:**
- Dispatcher owns validation pipeline — handlers receive already-validated commands
- Queries are not validated in the dispatcher — handler throws `ArgumentException` on bad params → `400 Bad Request`
- `ValidationException` is Ordinis-owned — `Ordinis.Api` never references `FluentValidation.ValidationException` directly
- `ConcurrencyException` is Ordinis-owned — `Ordinis.Api` never references `DbUpdateConcurrencyException` directly

**Git tag:** `v0.3-phase3-app-infrastructure`

---

## Phase 4 — Application layer: features ✅

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
  - Command carries `FileName`, `ContentType`, `SizeInBytes`, `FileStream` (no `DownloadUrl` — produced by storage service)
  - Handler calls `IFileStorageService.UploadAsync(...)` → receives `downloadUrl` → calls `task.AddAttachment(..., downloadUrl, now)`
  - Returns `Guid` (new attachment ID)
  - Validator: `FileName` non-empty, `SizeInBytes` > 0, `ContentType` non-empty
- [x] `RemoveAttachment` + `RemoveAttachmentHandler`
  - Handler loads attachment to read its `StorageUrl`, calls `task.RemoveAttachment(attachmentId)`, saves, then calls `IFileStorageService.DeleteAsync(storageUrl)`
  - DB saved first — orphaned files on disk are recoverable; orphaned DB rows pointing to missing files are not

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

### Step 2 — Projects & Boards ✅

**DTOs**
- [x] `ProjectSummaryDto` — list view (id, name, status, member count, task count, created)
- [x] `ProjectDto` — detail view (includes `BoardSummaryDto[]`, `ProjectMemberDto[]`)
- [x] `ProjectMemberDto` — id, userId, userName, role, joinedAt
- [x] `BoardSummaryDto` — id, name, isArchived, taskCount
- [x] `BoardDto` — detail view (includes `TaskSummaryDto[]`)
- [x] `ProjectMapper` — static extension methods

**Commands**
- [x] `CreateProject` + `CreateProjectHandler` + `CreateProjectValidator`
  - Returns `Guid`
  - Slug is auto-generated from `Name` by the handler (not a caller-supplied field)
  - Validator: `OrganizationId` required and exists, `Name` non-empty max 100 chars, generated slug unique within the organization
- [x] `UpdateProject` + `UpdateProjectHandler` + `UpdateProjectValidator`
  - Updates `Name`, `Description`
  - Catches concurrency exception → `ConcurrencyException`
- [x] `DeleteProject` + `DeleteProjectHandler`
  - Soft delete via `project.SoftDelete(now)`
- [x] `ArchiveProject` + `ArchiveProjectHandler` *(added beyond original plan — wraps the `Project.Archive()` domain method that already existed from Phase 2)*
- [x] `UnarchiveProject` + `UnarchiveProjectHandler` *(added beyond original plan — counterpart to `ArchiveProject`)*
- [x] `AddProjectMember` + `AddProjectMemberHandler` + `AddProjectMemberValidator`
  - Calls `project.AddMember(userId, role, now)`
  - Validator: `UserId` exists, `Role` valid enum value, user not already a member
- [x] `RemoveProjectMember` + `RemoveProjectMemberHandler`
  - Calls `project.RemoveMember(userId)`
- [x] `ChangeMemberRole` + `ChangeMemberRoleHandler` + `ChangeMemberRoleValidator` *(added beyond original plan — wraps the `Project.ChangeMemberRole()` domain method that already existed from Phase 2)*
- [x] `CreateBoard` + `CreateBoardHandler` + `CreateBoardValidator`
  - Creates the board directly via `Board.Create(projectId, name, createdByUserId)` — `Board` is an independent aggregate root
  - Returns `Guid`
  - Validator: `Name` non-empty max 100 chars; project exists and not archived; no duplicate name in project
- [x] `ArchiveBoard` + `ArchiveBoardHandler`
  - Loads and archives the board directly via `BoardId` only (`board.Archive()`) — no `ProjectId` needed
- [x] `RenameBoard` + `RenameBoardHandler` + `RenameBoardValidator`
  - Loads and renames the board directly via `BoardId` only (`board.Rename(name)`)
  - Validator: `Name` non-empty max 100 chars; no duplicate name in project

**Queries**
- [x] `GetProjectById` + `GetProjectByIdHandler` — returns `ProjectDto` with boards and members; throws `NotFoundException`
  - Per-board task counts resolved via a separate grouped query (`Board` carries no task navigation collection) and passed into `ProjectMapper.ToDto`
- [x] `GetProjectsFiltered` + `GetProjectsFilteredHandler`
  - Filter: `OrganizationId?`, `MemberId?`, `IncludeArchived` (via `ProjectFilter`, mirrors the `TaskFilter` shape — pagination/sort fields live on the filter record, not the query)
  - Returns `PagedResult<ProjectSummaryDto>`
- [x] `GetProjectTasks` + `GetProjectTasksHandler` — all tasks across all boards in a project; reuses `TaskFilter` scoped via `task.Board.ProjectId`
- [x] `GetProjectMembers` + `GetProjectMembersHandler` — returns `ProjectMemberDto[]`
- [x] `GetBoardById` + `GetBoardByIdHandler` — returns `BoardDto` with capped embedded tasks
- [x] `GetBoardTasks` + `GetBoardTasksHandler` — tasks for a specific board; reuses `TaskFilter`

**DI registration**
- [x] `AddProjectHandlers(this IServiceCollection)` — wired into `AddApplicationServices()`

**Found during review:** `GetTasksFiltered`'s `Page`/`PageSize`/`SortBy`/`SortDescending` were moved from the query record onto `TaskFilter` itself (matching the new `ProjectFilter` shape), so `GetProjectTasks`/`GetBoardTasks` could reuse `TaskFilter` without duplicating pagination params. Also fixed a bug found during this review where `ProjectMapper.ToDto`'s embedded boards always reported `TaskCount = 0` (the dead no-arg `Board.ToSummaryDto()` overload hardcoded it, and has been removed) — `GetProjectByIdHandler` now resolves real per-board counts via a grouped query.

**Removed `Project.Boards` navigation collection** (domain model fix, found during this review): `Project` held a live `List<Board> _boards` / `Boards` navigation even though `Board` is documented as an independent aggregate root reached everywhere else via `db.Boards` directly (`CreateBoardValidator`, `ArchiveBoardHandler`, `RenameBoardHandler`, `GetBoardById`, `GetBoardTasks`). Holding a sibling aggregate root as a live object-graph navigation violates the rule that aggregates reference each other by ID only — `Project.Members` is the correct pattern (`ProjectMember` is genuinely owned), `Project.Boards` was not. Removed the field/property from `Project`; `GetProjectByIdHandler` and `GetProjectsFilteredHandler` now query `db.Boards` directly by `ProjectId` instead. The same anti-pattern was found on `User.ProjectMemberships` (a dead, zero-caller navigation into `Project`'s owned `ProjectMember` collection) and removed too. Both are now documented as a standing convention in `CLAUDE.md`'s "Key design decisions" table ("Aggregate references").

---

### Step 3 — Organizations ✅

**DTOs**
- [x] `OrganizationDto` — id, name, description, isActive, createdAt, projectCount *(expanded beyond the original plan's id/name/createdAt/projectCount to include `Description` and `IsActive`, mirroring `ProjectDto`'s richer detail-view shape)*
- [x] `OrganizationMapper` — static `ToDto(this Organization, int projectCount)`

**Commands**
- [x] `CreateOrganization` + `CreateOrganizationHandler` + `CreateOrganizationValidator`
  - Returns `Guid`
  - Slug is auto-generated from `Name` via the new shared `ISlugGenerator` (see below), checked for global uniqueness (organizations have no parent scope, unlike `Project.Slug` which is scoped per-organization)
  - Validator: `Name` non-empty max 100 chars, generated slug globally unique
- [x] `RenameOrganization` + `RenameOrganizationHandler` + `RenameOrganizationValidator` *(replaces the originally planned single `UpdateOrganization` — split into `Rename` + `UpdateOrganizationDescription` below, one command per mutation, matching the granularity of the underlying `Organization.Rename()` / `Organization.UpdateDescription()` domain methods)*
  - Updates `Name` only — slug is immutable after creation
  - Catches `DbUpdateConcurrencyException` → `ConcurrencyException`
- [x] `UpdateOrganizationDescription` + `UpdateOrganizationDescriptionHandler` + `UpdateOrganizationDescriptionValidator` *(added beyond original plan)*
  - Updates `Description` (nullable, clears when `null`)
  - Catches concurrency exception → `ConcurrencyException`
- [x] `SuspendOrganization` + `SuspendOrganizationHandler` *(added beyond original plan — wraps the `Organization.Suspend()` domain method that already existed from Phase 2, same pattern as `ArchiveProject`/`UnarchiveProject` in Step 2)*
- [x] `ReactivateOrganization` + `ReactivateOrganizationHandler` *(added beyond original plan — counterpart to `SuspendOrganization`)*

**Queries**
- [x] `GetOrganizationById` + `GetOrganizationByIdHandler` — returns `OrganizationDto`; throws `NotFoundException`; project count resolved via a separate scalar `CountAsync` (no navigation collection across the Organization → Project aggregate boundary)
- [x] `GetOrganizationProjects` + `GetOrganizationProjectsHandler` — returns `PagedResult<ProjectSummaryDto>`; validates the organization exists (`NotFoundException` if not), reuses `ProjectFilter` for sort/page/`IncludeArchived`/`MemberId`, maps via the new `ProjectMapper.ToSummaryDto(this Project, int boardCount)` overload (board count resolved via a separate grouped query, same pattern as `ProjectMapper.ToDto`'s `boardTaskCounts`)

**DI registration**
- [x] `AddOrganizationHandlers(this IServiceCollection)` — wired into `AddApplicationServices()`

**Found during review:** Extracted slug generation into a shared `ISlugGenerator` / `SlugGenerator` (`Ordinis.Application/Common/`, registered as a singleton — stateless, compiled regex) so `CreateOrganization` and `CreateProject` derive slugs the same way instead of each running its own inline regex. `CreateProjectHandler`/`CreateProjectValidator` (Step 2) were retrofitted to inject `ISlugGenerator` as part of this change, removing their original private `Slugify` method.

---

### Step 4 — Users ✅

**DTOs**
- [x] `UserDto` — id, displayName, email, organizationId, createdAt *(expanded beyond the original plan to include `OrgRole`, `IsActive`, `OrganizationName`, `UpdatedAt`, mirroring the richer detail-view shape used by `ProjectDto`/`OrganizationDto`)*
- [x] `UserMapper`

**Commands**
- [x] `CreateUser` + `CreateUserHandler` + `CreateUserValidator`
  - Returns `Guid`
  - Validator: `Email` valid format and unique (scoped per organization), `DisplayName` non-empty max 100 chars, `OrganizationId` exists
- [x] `UpdateUser` + `UpdateUserHandler` + `UpdateUserValidator`
  - Updates `DisplayName`
  - Validator: `DisplayName` non-empty max 100 chars

**Queries**
- [x] `GetUserById` + `GetUserByIdHandler` — returns `UserDto`; throws `NotFoundException`
- [x] `GetUserTasks` + `GetUserTasksHandler` — tasks assigned to a user; same filter/sort/page params as `GetTasksFiltered`

**DI registration**
- [x] `AddUserHandlers(this IServiceCollection)` — wired into `AddApplicationServices()`

**Found during review:** Authentication groundwork was pulled forward from Phase 8 — `IPasswordHasher` (`Hash`/`Verify`) added to `Ordinis.Application/Common/` (implementation still pending in `Ordinis.Infrastructure`); `User` gained `PasswordHash`, `RefreshToken`, `RefreshTokenExpiresAt` fields plus `ChangePasswordHash`/`SetRefreshToken`/`RevokeRefreshToken` domain methods. `CreateUserHandler` hashes the incoming plaintext password via `IPasswordHasher.Hash()` before calling `User.Create(...)` — the domain never sees plaintext. `CreateUserValidator` also validates a `Password` field (min 8 chars), required because `CreateUser` now accepts one. `RefreshToken`/`RefreshTokenExpiresAt` are intentionally excluded from `UserDto` (auth-sensitive, never serialized to API responses). Three commands were added beyond the original plan, mirroring the `SuspendOrganization`/`ReactivateOrganization` pattern from Step 3: `DeactivateUser` + `DeactivateUserHandler` and `ReactivateUser` + `ReactivateUserHandler` (wrap the new `User.Deactivate()`/`Reactivate()` domain methods), and `ChangeUserOrgRole` + `ChangeUserOrgRoleHandler` + `ChangeUserOrgRoleValidator` (separates org-role changes from display-name updates, same granularity precedent as `RenameOrganization`/`UpdateOrganizationDescription` in Step 3). Phase 8 still owns the `IPasswordHasher` implementation, JWT issuance/refresh endpoints, and wiring `[Authorize]`/policies — only the domain/application groundwork has been pulled forward here.

---

### Step 5 — Shared application infrastructure

- [x] Add `NotFoundException` to `Ordinis.Application/Common/` — thrown by query handlers; global middleware maps to `404 Not Found` with Problem Details
- [x] Add `PagedResult<T>` to `Ordinis.Application/Common/` — wraps list query results with `Items`, `TotalCount`, `Page`, `PageSize`
- [x] Add `TaskFilter` parameter record to `Tasks/Queries/` — keep query objects slim, separate filter concerns from query dispatch. Pagination/sort fields (`Page`, `PageSize`, `SortBy`, `SortDescending`) live on the filter record itself (`GetTasksFiltered(TaskFilter? Filter)`) — moved off `GetTasksFiltered` during the Step 2 review so `GetProjectTasks`/`GetBoardTasks` could reuse `TaskFilter` unchanged
- [x] Add `ProjectFilter` parameter record to `Projects/Queries/` — mirrors `TaskFilter`'s shape
- [x] Finalize `AddApplicationServices()` — call all per-feature `AddXxxHandlers()` methods (`AddTaskHandlers()`, `AddProjectHandlers()`, `AddOrganizationHandlers()`, and `AddUserHandlers()` all wired — confirmed in `ApplicationServiceExtensions.cs`)

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
- [x] Add `IFileStorageService` to `Ordinis.Application/Common/` — contract: `UploadAsync(Stream, fileName, contentType) → string downloadUrl`; `DeleteAsync(downloadUrl)`
  - **Pulled forward:** implemented ahead of the rest of Phase 5, alongside the `AddAttachment`/`RemoveAttachment` handler rework (branch `feature/phase-4-attachment-storage-handlers`) — the handlers needed the contract to call synchronously. Only the interface exists; `LocalFileStorageService` and DI wiring are still pending below.
- [ ] Implement `LocalFileStorageService` in `Ordinis.Infrastructure/FileStorage/`:
  - Writes files to a configurable path (default `wwwroot/attachments/`) bound via `LocalStorageOptions`
  - Filename strategy: `{guid}_{sanitizedOriginalName}` — guarantees uniqueness, prevents path traversal, preserves readability
  - `DownloadUrl` returned as a relative path: `/attachments/{storedFileName}`
  - `DeleteAsync` resolves the file path from the URL and deletes the file; logs a warning if the file is not found rather than throwing
  - Register in `AddInfrastructureServices`: `services.AddScoped<IFileStorageService, LocalFileStorageService>()`
  - Register static file middleware in `Program.cs` to serve `wwwroot/` — required for `DownloadUrl` links to resolve
  - **Swap note:** replacing `LocalFileStorageService` with `AzureBlobStorageService` or `S3FileStorageService` is a one-class change — the interface contract and all handler code remain unchanged
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
- [ ] Add `InfrastructureServiceExtensions` — `AddInfrastructureServices(this IServiceCollection, IConfiguration)` called from `Program.cs`; registers `AppDbContext`, `TimeProvider.System` as singleton, `IFileStorageService` (`LocalFileStorageService`), `LocalStorageOptions` (from config), Serilog, health checks, background job host

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

#### Part 1 — Test infrastructure + Task validator tests
> Establishes shared test infrastructure reused by all subsequent parts.

**Shared infrastructure (build first)**
- [x] `TestDbContextFactory` — creates a fresh EF Core InMemory `AppDbContext` per test; each test gets an isolated database name (`Guid.NewGuid().ToString()`) to prevent state leakage between tests (done — `tests/Ordinis.UnitTests/Common/TestDbContextFactory.cs`; factory construction split out of the `TestAppDbContext` double itself, and the existing `AddAttachmentHandler`/`RemoveAttachmentHandler` tests switched over from the old `TestAppDbContext.CreateInMemory()` static method)
- [x] `DomainFactory` — static helper methods that create and seed realistic domain objects via their real aggregate factories (`Organization.Create(...)`, `Project.Create(...)`, `Board.Create(...)`, `User.Create(...)`, `ProjectTask.Create(...)`); used by both validator and handler tests to avoid repeating seeding boilerplate (done — kept the existing per-entity `Common/Builders/*Builder` classes, already established for `Task`/`Board`/`Comment`/`Project`/`User`, rather than introducing a single combined class; added the missing `OrganizationBuilder` to complete the set)

**Task validators** (each tested in isolation via `FluentValidation.TestHelper`; `MustAsync` checks seeded via `TestDbContextFactory`)
- [x] `CreateTaskValidator` — `BoardId` required and exists (not archived); `Title` non-empty max 200 chars; `Priority` valid enum value (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/CreateTaskValidatorTests.cs`, using `FluentValidation.TestHelper`'s `TestValidateAsync` since the `BoardId`/`AssigneeId` rules are async `MustAsync` checks against the database; also covers the `AssigneeId` and `RequestedByUserId` rules)
- [x] `UpdateTaskValidator` — `Title` non-empty max 200 chars; `Priority` valid enum value (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/UpdateTaskValidatorTests.cs`; also covers `TaskId`/`RequestedByUserId` and a valid-command no-errors baseline)
- [x] `MoveTaskValidator` — `NewStatus` is a valid `ProjectTaskStatus` enum value (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/MoveTaskValidatorTests.cs`; also covers `TaskId`)
- [x] `AssignTaskValidator` — `AssigneeId` required; user exists and is a project member (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/AssignTaskValidatorTests.cs`; the validator itself only checks user existence — project-membership is deliberately deferred to the authorization layer per its own remarks, so the tests don't cover that; also covers `TaskId`/`RequestedByUserId`)
- [x] `AddCommentValidator` — `Content` non-empty, max 10 000 chars (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/AddCommentValidatorTests.cs`; also covers `TaskId` and the 10,000-char boundary; no `AuthorId` rule exists in the validator, so none is tested)
- [x] `EditCommentValidator` — `Content` non-empty, max 10 000 chars; requesting user is the comment author (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/EditCommentValidatorTests.cs`; also covers `TaskId`/`CommentId`/`RequestedByUserId` and the 10,000-char boundary)
- [x] `AddAttachmentValidator` — `FileName` non-empty; `SizeInBytes` > 0; `ContentType` non-empty (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/AddAttachmentValidatorTests.cs`; also covers `TaskId`/`FileStream`/`UploadedByUserId` and the `FileName`/`ContentType` length boundaries)

**Git tag (after Part 1):** `v0.9-part1-task-validators`

---

#### Part 2 — Project, Board, Organization, and User validator tests
> Reuses all infrastructure from Part 1. Mechanical — follows the same shape.

**Project & Board validators**
- [x] `CreateProjectValidator` — `OrganizationId` required and exists; `Name` non-empty max 100 chars; generated slug unique within the organization (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/CreateProjectValidatorTests.cs`; also covers `CreatedByUserId` and `Description` max length 1000; fixed a bug found while testing — the `Name` rule lacked `Cascade(CascadeMode.Stop)`, so an empty `Name` let the slug-uniqueness `MustAsync` run anyway and throw inside `SlugGenerator.Slugify` instead of failing validation cleanly)
- [x] `AddProjectMemberValidator` — `UserId` exists; `Role` valid enum value; user not already a member (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/AddProjectMemberValidatorTests.cs`; also covers `ProjectId`; fixed a gap found while testing — the validator had no rule for `Role` at all, so an out-of-range value passed silently; added `RuleFor(x => x.Role).IsInEnum()`, matching `ChangeMemberRoleValidator`)
- [x] `ChangeMemberRoleValidator` — `Role` valid enum value (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/ChangeMemberRoleValidatorTests.cs`; also covers `ProjectId`/`UserId`; purely synchronous, no DB state needed; the unused `IAppDbContext db` constructor parameter flagged earlier has since been removed)
- [x] `CreateBoardValidator` — `Name` non-empty max 100 chars; project exists and is not archived; no duplicate board name within the project (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/CreateBoardValidatorTests.cs`; also covers `ProjectId`/`CreatedByUserId` and case-insensitive duplicate-name scoping per project; no bugs found)
- [x] `RenameBoardValidator` — `Name` non-empty max 100 chars; no duplicate board name within the project (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/RenameBoardValidatorTests.cs`; also covers `BoardId`, renaming to its own current name, and case-insensitive duplicate scoping per project; fixed a latent bug found while testing — the board-lookup `Select(b => b.ProjectId)` projected a non-nullable `Guid`, so `SingleOrDefaultAsync` returned `Guid.Empty` instead of `null` when the board didn't exist, leaving the intended `if (projectId is null)` branch dead; cast to `(Guid?)` in the `Select` so it behaves as written)

**Organization validators**
- [x] `CreateOrganizationValidator` — `Name` non-empty max 100 chars; generated slug globally unique (done — `tests/Ordinis.UnitTests/Application/Organizations/Validators/CreateOrganizationValidatorTests.cs`; also covers `Description` max length 1000; fixed the same `Cascade(CascadeMode.Stop)` gap as `CreateProjectValidator` — an empty `Name` let the slug-uniqueness `MustAsync` run anyway and throw inside `SlugGenerator.Slugify`. Audited all 19 validators in `Ordinis.Application` for the same pattern — `Slugify` is the only throwing call reached from a validator's `MustAsync`/`Must`, and it only appears in these two validators, so no other instances exist)
- [x] `RenameOrganizationValidator` — `Name` non-empty max 100 chars (done — `tests/Ordinis.UnitTests/Application/Organizations/Validators/RenameOrganizationValidatorTests.cs`; also covers `OrganizationId`; purely synchronous, no DB state needed; no bugs found)
- [x] `UpdateOrganizationDescriptionValidator` — `Description` max length (if constrained) (done — `tests/Ordinis.UnitTests/Application/Organizations/Validators/UpdateOrganizationDescriptionValidatorTests.cs`; also covers `OrganizationId`; purely synchronous, no DB state needed; no bugs found)

**User validators**
- [x] `CreateUserValidator` — `Email` valid format and unique within the organization; `DisplayName` non-empty max 100 chars; `OrganizationId` exists; `Password` min 8 chars (done — `tests/Ordinis.UnitTests/Application/Users/Validators/CreateUserValidatorTests.cs`; also covers `OrganizationId` suspended-org rejection, case-insensitive email uniqueness scoped per organization, and `OrgRole` enum validity; no bugs found)
- [x] `UpdateUserValidator` — `DisplayName` non-empty max 100 chars (done — `tests/Ordinis.UnitTests/Application/Users/Validators/UpdateUserValidatorTests.cs`; also covers `UserId`/`RequestedByUserId`; purely synchronous, no DB state needed; no bugs found)
- [x] `ChangeUserOrgRoleValidator` — `Role` valid enum value (done — `tests/Ordinis.UnitTests/Application/Users/Validators/ChangeUserOrgRoleValidatorTests.cs`; also covers `UserId`/`RequestedByUserId`; purely synchronous, no DB state needed; no bugs found)

**Git tag (after Part 2):** `v0.9-part2-remaining-validators`

---

#### Part 3 — Mapper tests
> Pure function tests — no EF Core, no DI, no async. No shared infrastructure needed.

**TaskMapper**
- [ ] `ToSummaryDto` — all fields map correctly; null `AssigneeId` maps to null `AssigneeName`; no nested collections
- [ ] `ToDto` — embedded `CommentDto` list maps correctly; `IsEdited` flag derived from `Comment.IsEdited`; embedded `AttachmentDto` list maps correctly; `userLookup` resolves assignee and comment author display names; missing user ID in lookup maps gracefully

**ProjectMapper**
- [ ] `ToSummaryDto` — all fields map correctly; `boardCount` and `memberCount` parameters flow through
- [ ] `ToDto` — embedded `BoardSummaryDto[]` maps correctly with per-board `taskCount`; embedded `ProjectMemberDto[]` maps correctly; truncation flags set correctly when board/member counts exceed cap

**OrganizationMapper**
- [ ] `ToDto` — all fields map correctly; `projectCount` parameter flows through; `IsActive` and `Description` included

**UserMapper**
- [ ] `ToDto` — all fields map correctly; `OrganizationName` parameter flows through; auth-sensitive fields (`RefreshToken`, `RefreshTokenExpiresAt`, `PasswordHash`) are absent from the DTO

**Git tag (after Part 3):** `v0.9-part3-mappers`

---

#### Part 4 — Task handler tests
> Follows the `AddAttachmentHandler` / `RemoveAttachmentHandler` pattern already established. Reuses `TestDbContextFactory` and `DomainFactory` from Part 1.

**Command handlers**
- [x] `AddAttachmentHandler` — attachment stored via `IFileStorageService`; `AttachmentAdded` domain event raised; attachment ID returned (done)
- [x] `RemoveAttachmentHandler` — attachment removed from task; `AttachmentRemoved` domain event raised; `IFileStorageService.DeleteAsync` called after DB save (done)
- [ ] `CreateTaskHandler` — task created with correct fields; `TaskCreated` domain event raised; new task ID returned
- [ ] `UpdateTaskHandler` — `Title`, `Description`, `Priority`, `DueDate` updated; `DbUpdateConcurrencyException` caught and translated to `ConcurrencyException`
- [ ] `DeleteTaskHandler` — soft delete applied (`IsDeleted = true`, `DeletedAt` set); task no longer returned by default EF queries
- [ ] `MoveTaskHandler` — status transition applied; `TaskMoved` domain event raised with correct previous and new status
- [ ] `AssignTaskHandler` — `AssigneeId` set; `TaskAssigned` domain event raised
- [ ] `UnassignTaskHandler` — `AssigneeId` cleared; `TaskUnassigned` domain event raised
- [ ] `AddCommentHandler` — comment added to task; `CommentAdded` domain event raised; new comment ID returned
- [ ] `EditCommentHandler` — `Content` updated; `IsEdited` set to `true`
- [ ] `RemoveCommentHandler` — comment soft deleted; `CommentRemoved` domain event raised

**Query handlers**
- [ ] `GetTaskByIdHandler` — returns correct `TaskDto` with embedded comments and attachments; throws `NotFoundException` when task does not exist or is soft deleted
- [ ] `GetTasksFilteredHandler` — returns correct page of `TaskSummaryDto`; each filter param (`BoardId`, `AssigneeId`, `Status`, `Priority`, `DueBefore`, `DueAfter`) applied correctly in isolation; `TotalCount` matches pre-pagination count; sort ascending and descending; `PageSize` cap enforced

**Git tag (after Part 4):** `v0.9-part4-task-handlers`

---

#### Part 5 — Project and Board handler tests
> Same pattern as Part 4.

**Project command handlers**
- [ ] `CreateProjectHandler` — project created; slug auto-generated from name via `ISlugGenerator`; new project ID returned
- [ ] `UpdateProjectHandler` — `Name` and `Description` updated; `DbUpdateConcurrencyException` translated to `ConcurrencyException`
- [ ] `DeleteProjectHandler` — soft delete applied
- [ ] `ArchiveProjectHandler` — project archived; `IsArchived = true`
- [ ] `UnarchiveProjectHandler` — project unarchived; `IsArchived = false`
- [ ] `AddProjectMemberHandler` — member added to project with correct role and `JoinedAt`
- [ ] `RemoveProjectMemberHandler` — member removed from project
- [ ] `ChangeMemberRoleHandler` — member role updated

**Board command handlers**
- [ ] `CreateBoardHandler` — board created directly as independent aggregate root; new board ID returned
- [ ] `ArchiveBoardHandler` — board archived; `IsArchived = true`
- [ ] `RenameBoardHandler` — board name updated

**Project query handlers**
- [ ] `GetProjectByIdHandler` — returns correct `ProjectDto` with embedded boards and members; per-board task counts resolved correctly via grouped query; throws `NotFoundException` when not found
- [ ] `GetProjectsFilteredHandler` — `OrganizationId` filter; `MemberId` filter; `IncludeArchived` flag; pagination and sort
- [ ] `GetProjectTasksHandler` — returns paged tasks scoped to all boards in the project
- [ ] `GetProjectMembersHandler` — returns all members for the project

**Board query handlers**
- [ ] `GetBoardByIdHandler` — returns correct `BoardDto` with embedded tasks (capped); throws `NotFoundException` when not found
- [ ] `GetBoardTasksHandler` — returns paged tasks scoped to the board; filter and sort applied

**Git tag (after Part 5):** `v0.9-part5-project-board-handlers`

---

#### Part 6 — Organization and User handler tests + Dispatcher tests

**Organization command handlers**
- [ ] `CreateOrganizationHandler` — organization created; slug auto-generated and globally unique; new organization ID returned
- [ ] `RenameOrganizationHandler` — `Name` updated; `DbUpdateConcurrencyException` translated to `ConcurrencyException`
- [ ] `UpdateOrganizationDescriptionHandler` — `Description` updated; clears when `null`; concurrency exception translated
- [ ] `SuspendOrganizationHandler` — organization suspended; `IsActive = false`
- [ ] `ReactivateOrganizationHandler` — organization reactivated; `IsActive = true`

**Organization query handlers**
- [ ] `GetOrganizationByIdHandler` — returns correct `OrganizationDto`; `projectCount` resolved via separate scalar query; throws `NotFoundException` when not found
- [ ] `GetOrganizationProjectsHandler` — returns paged `ProjectSummaryDto`; `IncludeArchived` flag; `MemberId` filter; throws `NotFoundException` when organization not found

**User command handlers**
- [ ] `CreateUserHandler` — plaintext password hashed via `IPasswordHasher` before `User.Create()`; domain never receives plaintext; new user ID returned
- [ ] `UpdateUserHandler` — `DisplayName` updated
- [ ] `DeactivateUserHandler` — user deactivated; `IsActive = false`
- [ ] `ReactivateUserHandler` — user reactivated; `IsActive = true`
- [ ] `ChangeUserOrgRoleHandler` — org role updated

**User query handlers**
- [ ] `GetUserByIdHandler` — returns correct `UserDto`; auth-sensitive fields absent; throws `NotFoundException` when not found
- [ ] `GetUserTasksHandler` — returns paged tasks assigned to the user; filter and sort applied

**Dispatcher**
- [ ] Valid command with passing validator reaches handler and returns result
- [ ] Invalid command fires `IValidator<T>` before handler; handler is never invoked; `ValidationException` thrown with correct field-level errors
- [ ] Valid command with no registered validator reaches handler directly (no validation skip error)
- [ ] Query bypasses the validation pipeline entirely regardless of whether a validator is registered
- [ ] Command with no registered handler throws `InvalidOperationException`

**Git tag (after Part 6):** `v0.9-part6-org-user-handlers-dispatcher`

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
| `v0.0-phase1-solution-setup` | Phase 1: Repository & solution setup |
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
