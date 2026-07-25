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
│                           # OutboxDispatcherJob.cs
│
├── Ordinis.Infrastructure.Migrations.SqlServer
│   ├── DesignTime/         # AppDbContextFactory.cs (IDesignTimeDbContextFactory<AppDbContext>)
│   └── Migrations/         # InitialCreate.cs, AppDbContextModelSnapshot.cs
│
├── Ordinis.Infrastructure.Migrations.PostgreSql
│   ├── DesignTime/         # AppDbContextFactory.cs (IDesignTimeDbContextFactory<AppDbContext>)
│   └── Migrations/         # InitialCreate.cs, AppDbContextModelSnapshot.cs
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
  - Validator: `Name` non-empty max 100 chars; project exists and not archived; no duplicate name in project; `CreatedByUserId` exists
- [x] `ArchiveBoard` + `ArchiveBoardHandler`
  - Loads and archives the board directly via `BoardId` only (`board.Archive()`) — no `ProjectId` needed
- [x] `UnarchiveBoard` + `UnarchiveBoardHandler` *(added beyond original plan during the `BoardsController` review — wraps the `Board.Unarchive()` domain method, which already existed but had no Application-layer command exposing it; counterpart to `ArchiveBoard`, mirrors `UnarchiveProject`)*
- [x] `RenameBoard` + `RenameBoardHandler` + `RenameBoardValidator`
  - Loads and renames the board directly via `BoardId` only (`board.Rename(name)`)
  - Validator: `Name` non-empty max 100 chars; no duplicate name in project
  - **Found during the `BoardsController` review**: `CreateBoardValidator.CreatedByUserId` had no existence check (only `.NotEmpty()`) — same bug shape as `CreateProjectValidator`'s earlier fix. Since `Board.CreatedByUserId` is a required FK with `DeleteBehavior.Restrict`, a nonexistent user ID passed validation and blew up as an unhandled `500` at `SaveChangesAsync` instead of a clean `422`. Fixed with the same `MustAsync` existence check pattern. Also found: `RenameBoardHandler` never wrapped `SaveChangesAsync` in a `try/catch (DbUpdateConcurrencyException)` at all (unlike `UpdateProjectHandler`/`UpdateUserHandler`/`ChangeUserOrgRoleHandler`), so a genuine optimistic-concurrency race produced an unhandled `500` instead of `409` — fixed to match the established pattern.

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
- [x] `UpdateOrganization` + `UpdateOrganizationHandler` + `UpdateOrganizationValidator` *(added
  later — consolidates `RenameOrganization` + `UpdateOrganizationDescription` into one command for
  `OrganizationsController.Update`)*
  - **Bug found during Phase 9 test-writing review:** `OrganizationsController.Update` sent
    `RenameOrganization` and `UpdateOrganizationDescription` as two independent `SendAsync` calls,
    each with its own `SaveChangesAsync`. A valid name plus an over-length description committed
    the rename before the description update failed validation — a real, deterministic partial
    write on a single HTTP request, not a race. Fixed by adding `UpdateOrganization`, which loads
    the organization once, applies both `Rename()` and `UpdateDescription()`, and saves once —
    atomic by construction, mirroring the earlier `ProjectTask.Update()` consolidation fix for the
    identical class of bug (see Step 1 above).
  - `RenameOrganization`/`UpdateOrganizationDescription` are kept as-is (not deleted) — nothing
    else in `src` calls them individually, but their existing unit tests remain valid coverage of
    the underlying domain methods.
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

- [x] Install packages: `Microsoft.EntityFrameworkCore.SqlServer` (10.0.9), `Npgsql.EntityFrameworkCore.PostgreSQL` (10.0.2), `Microsoft.EntityFrameworkCore.Design` (10.0.9), `Dapper` (2.1.79) — all in `Ordinis.Infrastructure`; `IHostedService` chosen over Hangfire for the Outbox dispatcher (no extra package needed)
- [x] Configure `AppDbContext` (`Ordinis.Infrastructure/Persistence/AppDbContext.cs`):
  - Constructor injects `TimeProvider` — sets `CreatedAt` / `UpdatedAt` in `SaveChangesAsync` override
  - `DbSet<>` for all aggregate roots: `Organizations`, `Projects`, `Boards`, `Tasks`, `Users`, `OutboxMessages`
  - `OutboxMessages` not exposed on `IAppDbContext` — only `AppDbContext` and `OutboxDispatcherJob` write to it
  - `OnModelCreating` delegates entirely to `ApplyConfigurationsFromAssembly` — no inline configuration
  - Provider selected at startup via `appsettings.json` — wired in `InfrastructureServiceExtensions` (pending)
- [x] Define `IEntityTypeConfiguration<T>` classes — one per entity, in feature folders:
  - `OrganizationConfiguration` — PK, `Name`/`Slug` max length, `Slug` unique index, `IsActive` default, `RowVersion`
  - `ProjectConfiguration` — PK, FK to `Organization`/`CreatedByUser`, `RowVersion`, soft delete filter, `(OrganizationId, Slug)` composite unique index
  - `BoardConfiguration` — PK, FK to `Project` (Cascade) / `CreatedByUser` (Restrict), `RowVersion`, `IsArchived` default, soft delete filter
  - `ProjectTaskConfiguration` — PK, FK to `Board` (Cascade) / `Reporter` (Restrict) / `Assignee` (SetNull, nullable), `RowVersion`, soft delete filter, `Status`/`Priority` stored as `varchar` via `.HasConversion<string>()`, `HasMany(Comments)`/`HasMany(Attachments)` with Cascade
  - `CommentConfiguration` — PK, FK to `Author` (Restrict; Task FK configured from `ProjectTaskConfiguration`), soft delete filter, `Content` max 10 000
  - `AttachmentConfiguration` — PK, FK to `UploadedByUser` (Restrict; Task FK configured from `ProjectTaskConfiguration`), `FileName`/`ContentType`/`StorageUrl` max lengths; soft-delete filter chained through the required `Task` navigation (`Attachment` inherits `Entity`, not `AuditableEntity`, so it has no `IsDeleted` column of its own)
  - `ProjectMemberConfiguration` — composite PK (`ProjectId`, `UserId`), `Entity.Id` mapped as `ValueGeneratedNever()`, FK to `Project` (Cascade) / `User` (Restrict), `Role` stored as `varchar`, soft delete filter chained through the required `Project` navigation
  - `UserConfiguration` — PK, FK to `Organization` (Restrict), `Email` max 256, composite unique index `(OrganizationId, Email)`, `OrgRole` stored as `varchar`, `RowVersion`
  - `OutboxMessageConfiguration` — PK, `Type` max 500, `Payload` uncapped (`nvarchar(max)` / `text`), index on `ProcessedAt` for unprocessed-row polling
- [x] Add `IFileStorageService` to `Ordinis.Application/Common/` — contract: `UploadAsync(Stream, fileName, contentType) → string downloadUrl`; `DeleteAsync(downloadUrl)`
  - **Pulled forward:** implemented ahead of the rest of Phase 5, alongside the `AddAttachment`/`RemoveAttachment` handler rework (branch `feature/phase-4-attachment-storage-handlers`) — the handlers needed the contract to call synchronously. Only the interface exists; `LocalFileStorageService` and DI wiring are still pending below.
- [x] Implement `LocalFileStorageService` in `Ordinis.Infrastructure/FileStorage/` (`LocalFileStorageService.cs`, `LocalStorageOptions.cs`):
  - `LocalStorageOptions` bound from `LocalStorage` config section; `BasePath` defaults to `wwwroot/attachments`, `UrlPrefix` defaults to `/attachments`
  - Filename strategy: `{uuidv7}_{sanitizedOriginalName}` — `Path.GetFileName` strips path-traversal prefixes; invalid chars and spaces replaced with underscores
  - `UploadAsync` creates the directory on first use; writes via async `FileStream`; returns `{UrlPrefix}/{storedFileName}`
  - `DeleteAsync` logs a warning and returns successfully if the file is not found — prevents orphaned DB rows from a missing file blocking the delete operation
  - Register in `AddInfrastructureServices` (pending): `services.AddScoped<IFileStorageService, LocalFileStorageService>()`
  - Register `app.UseStaticFiles()` in `Program.cs` — pulled forward from Phase 6; required for `DownloadUrl` links to resolve; added to `Program.cs` in this phase
  - **Swap note:** replacing `LocalFileStorageService` with `AzureBlobStorageService` or `S3FileStorageService` is a one-class change — the interface contract and all handler code remain unchanged
- [x] Define `OutboxMessage` entity in `Persistence/` (`OutboxMessage.cs`):
  - `Id` (Guid, UUIDv7), `OccurredAt`, `Type` (CLR `FullName` of the event — used by dispatcher to deserialize), `Payload` (JSON via `System.Text.Json`), `ProcessedAt?`
- [x] Add Outbox dispatch to `AppDbContext.SaveChangesAsync`:
  - Intercepts `AggregateRoot` instances with pending domain events via `ChangeTracker`
  - Serializes each event to `OutboxMessage.From(domainEvent)` and adds to change tracker
  - Calls `aggregate.ClearDomainEvents()` after serialization, before `base.SaveChangesAsync` — committed atomically in same transaction
- [x] Add `IDomainEventHandler<TEvent>` to `Application/Common/` — handler contract for domain events dispatched from the Outbox; mirrors `ICommandHandler<T>` shape
- [x] Add `OutboxDispatcherJob` (`Persistence/OutboxDispatcherJob.cs`) — `BackgroundService` that polls `OutboxMessages WHERE ProcessedAt IS NULL`, dispatches events, marks rows processed:
  - `IServiceScopeFactory` injected — creates an async scope per tick so `AppDbContext` (scoped) is safe to use
  - `TimeProvider` injected directly (singleton) — used to stamp `ProcessedAt` and `Error` without a per-tick scope allocation
  - `ResolveEventType`: resolves CLR type from `FullName` via `AppDomain` assembly scan; results cached in `static ConcurrentDictionary<string, Type?>` so the scan runs once per event type
  - `InvokeHandlersAsync`: dispatches to all registered `IDomainEventHandler<TEvent>` via `MakeGenericType` + `MethodInfo.Invoke`; `MethodInfo` cached per handler type in `static ConcurrentDictionary<Type, MethodInfo>`; `TargetInvocationException` unwrapped via `ExceptionDispatchInfo.Capture` so real handler exceptions propagate cleanly
  - Retry logic: handler exceptions increment `OutboxMessage.RetryCount` and record `OutboxMessage.Error`; message is retried up to `MaxRetries = 3` then marked dead (`ProcessedAt = now`) — prevents transient failures from silently discarding events
  - `ExecuteAsync` wraps `ProcessBatchAsync` in `try/catch (Exception ex) when (ex is not OperationCanceledException)` — a transient DB error logs and continues rather than stopping the host (default `BackgroundServiceExceptionBehavior.StopHost` in .NET 6+)
  - `OutboxMessage.RetryCount` (int, default 0) and `OutboxMessage.Error` (string?, max 2000) added to entity and `OutboxMessageConfiguration`
  - **Multi-instance safety:** `FetchBatchAsync` issues provider-specific locking SQL (`WITH (UPDLOCK, READPAST)` for SQL Server, `FOR UPDATE SKIP LOCKED` for PostgreSQL) via `FromSqlInterpolated`; the active provider is read from `IOptions<OutboxOptions>` (populated by `InfrastructureServiceExtensions`); `ProcessBatchAsync` wraps the entire fetch-dispatch-save cycle in `BeginTransactionAsync`/`CommitAsync` so the row-level locks are held until `SaveChangesAsync` commits — without the explicit transaction both hints are ineffective (autocommit releases locks immediately after SELECT)
  - `OutboxOptions` internal class added to `Persistence/` — carries the normalized provider string set by `AddInfrastructureServices`
  - `AddHostedService<OutboxDispatcherJob>()` registered in `AddInfrastructureServices`
- [x] Configure global EF Core query filters for soft deletes — applied in entity configurations via `HasQueryFilter`: `Project` (`!IsDeleted`), `ProjectTask` (`!IsDeleted`), `Comment` (`!IsDeleted`), `Board` (`!IsDeleted`); `ProjectMember` and `Attachment` have no `IsDeleted` column of their own, so their filters are chained through their required parent navigation instead (`!Project.IsDeleted` / `!Task.IsDeleted`)
- [x] Add and manage migrations per provider — maintain separate migration folders for SQL Server and PostgreSQL
  - Two satellite class libraries added under `src/`: `Ordinis.Infrastructure.Migrations.SqlServer` and `Ordinis.Infrastructure.Migrations.PostgreSql`, each referencing `Ordinis.Infrastructure` and only its own EF Core provider package — EF Core does not allow two `ModelSnapshot`s for the same `DbContext` in a single assembly, so each provider needs its own migrations assembly
  - Each satellite project has an `IDesignTimeDbContextFactory<AppDbContext>` (`DesignTime/AppDbContextFactory.cs`) building the context directly with a dummy connection string — `dotnet ef migrations add` never touches `Program.cs`/`AddInfrastructureServices` or requires real User Secrets
  - `InfrastructureServiceExtensions.AddDatabase` sets `.MigrationsAssembly(...)` on `UseSqlServer`/`UseNpgsql` to route each provider to its own migration set at runtime; `Ordinis.Api` takes a `ProjectReference` to both satellite projects (not used in code — needed so both migration DLLs land in the publish output for the by-name assembly load) and to `Microsoft.EntityFrameworkCore.Design` (design-time only, `dotnet ef` requires it on the startup project)
  - **Bug found and fixed along the way:** all 5 aggregate configs used `.IsRowVersion()` on the `byte[] RowVersion` property. Npgsql only supports `.IsRowVersion()` on a `uint` mapped to the PostgreSQL `xmin` system column — on `byte[]` it silently never updates, breaking optimistic concurrency (no `409 Conflict`) under PostgreSQL. Switched to an app-managed concurrency token: `AggregateRoot.RowVersion` setter is now `internal` (matches `CreatedAt`/`UpdatedAt`), `AppDbContext.SaveChangesAsync` assigns a fresh `Guid.CreateVersion7().ToByteArray()` to every added/modified `AggregateRoot` via a new `SetConcurrencyTokens()` step, and all 5 configs use `.IsConcurrencyToken()` instead — identical behavior on both providers, `byte[]` contract in Domain/DTOs unchanged
  - Generated `InitialCreate` for both providers via `dotnet ef migrations add InitialCreate --project src/Ordinis.Infrastructure.Migrations.{SqlServer,PostgreSql}`; verified `RowVersion` is now a plain persisted column (`varbinary(max)` / `bytea`) on both, not DB-generated
- [x] Add Dapper connection access — `IAppDbContext.GetDbConnection()` added, implemented by `AppDbContext` as `Database.GetDbConnection()` (reuses EF Core's existing connection/transaction rather than opening a second one); `TestAppDbContext` (EF Core InMemory, unit tests) implements it as a `NotSupportedException` since InMemory has no ADO.NET connection
  - **No query handler converted yet.** All 12 existing query handlers are single-table-or-simple-join LINQ (filter → count → sort → page → batched user-name lookup) with no aggregation awkward enough to justify raw SQL. First real Dapper usage is deferred to **Phase 7 — Audit log** (`GET /api/v1/projects/{id}/audit`, a multi-table join over `OutboxMessages`/`Tasks`/`Boards`) — the first query that actually needs it. Phase 9 additionally benchmarks EF Core vs. Dapper on `GetTasksFiltered` for comparison, but that's a benchmark, not a production conversion.
- [x] Add health check endpoint (`/health`) — `AddDbContextCheck<AppDbContext>("database")` in `AddInfrastructureServices`; `app.MapHealthChecks("/health")` in `Program.cs`; requires `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (10.0.9)
- [x] Add `InfrastructureServiceExtensions` — `AddInfrastructureServices(this IServiceCollection, IConfiguration)` in `Infrastructure/Common/`; called from `Program.cs`; registers:
  - `AppDbContext` — dual-provider (`DatabaseProvider` config key); provider validated and normalized to canonical casing eagerly at startup before `AddDbContext` (an invalid value throws immediately, not on first DB access)
  - `IAppDbContext` forwarding delegate → same scoped `AppDbContext` instance (all 46 handlers inject `IAppDbContext`)
  - Connection string validated with `string.IsNullOrEmpty` (not `?? throw`) — catches the empty-placeholder case from committed `appsettings.json`
  - `TimeProvider.System` as singleton
  - `LocalStorageOptions` bound from `"LocalStorage"` config section (via `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.9)
  - `IFileStorageService` → `LocalFileStorageService` (scoped)
  - `OutboxOptions` configured with normalized provider string for `OutboxDispatcherJob`
  - Health checks (`AddDbContextCheck<AppDbContext>`) and `OutboxDispatcherJob` hosted service
  - `appsettings.json` configured: `DatabaseProvider`, `ConnectionStrings:DefaultConnection` (empty placeholder — set via User Secrets), `LocalStorage` section
  - `appsettings.Development.json` configured: `DatabaseProvider`, EF Core SQL command logging at `Information` level

**Git tag:** `v0.5-phase5-infrastructure`

---

## Phase 6 — API layer: core endpoints

> ⚠️ Requires Phase 4 and Phase 5.
> ✅ Phase 9 (Testing), Phase 10 (Docs), Phase 11 (CI/CD) can run alongside.

### Shared API infrastructure (do first)
- [x] **(moved from Phase 5)** Configure Serilog — packages/config in `Ordinis.Api` (composition root), not `Ordinis.Infrastructure`
- [x] **(moved from Phase 5)** Add `CorrelationIdMiddleware` — generates or propagates `X-Correlation-ID` per request; attaches to `ILogger` scope and response headers
- [x] **(moved from Phase 5)** Add request/response logging middleware — logs method, path, status code, duration, correlation ID at `Information` level
- [x] Add `GlobalExceptionMiddleware` — catches `ValidationException` → `422`, `ConcurrencyException` → `409`, `NotFoundException` → `404`, `DomainException` → `422`, unhandled → `500`; all responses use Problem Details (RFC 9457)
- [x] Add `ProblemDetailsFactory` helper — builds consistent `ProblemDetails` objects across all error cases
- [x] Add `CorrelationId` to all Problem Details responses via middleware
- [x] Register middleware in `Program.cs` in correct order: correlation ID → request logging → global exception → routing → auth (Phase 8) → endpoints
- [x] Add `ApiServiceExtensions` — `AddApiServices(this IServiceCollection)` wires controllers, rate limiting, response caching, CORS

**Found during `OrganizationsController` work:** `GlobalExceptionMiddleware` did not catch
`DomainException` despite that type's own XML doc comment promising a `422` mapping — it was
silently falling through to the generic `500` handler. Added a `catch (DomainException ex)` clause
(before the generic catch-all) and extended `ProblemDetailsFactory.Create` with an optional `type`
parameter so `DomainException.ErrorCode` surfaces in the Problem Details `type` field as
`urn:ordinis:error:{ErrorCode}` (e.g. `urn:ordinis:error:organization.already-suspended`).

**Second bug found during manual verification of the above:** `GlobalExceptionMiddleware.WriteAsync`
took a `ProblemDetails`-typed parameter and called `context.Response.WriteAsJsonAsync(problemDetails)`
— the generic overload infers `TValue` from the *compile-time* parameter type, so when a
`ValidationProblemDetails` (a subclass) was passed in, its `Errors` dictionary was silently sliced
off during serialization; every `422` validation response was missing its field-level error details
entirely. Fixed by serializing via the runtime type instead:
`WriteAsJsonAsync(problemDetails, problemDetails.GetType())`. Verified via `curl` that
`POST /organizations` with an empty `name` now returns `"errors":{"Name":[...]}` in the body.

### Controllers (one file per resource)
- [x] `OrganizationsController`
  - `GET    /api/v1/organizations/{id}` → `GetOrganizationById`
  - `GET    /api/v1/organizations/{id}/projects` → `GetOrganizationProjects` (paged)
  - `POST   /api/v1/organizations` → `CreateOrganization` → `201 Created` with `Location` header
  - `PUT    /api/v1/organizations/{id}` → `UpdateOrganization` → `204 No Content`
    *(originally composed two separate `RenameOrganization` + `UpdateOrganizationDescription`
    dispatcher calls from one request body — found during Phase 9 test-writing review to be
    non-atomic (a valid name + an over-length description committed the rename before the
    description update's 422), so the Application layer added a single `UpdateOrganization`
    command and the controller now sends just that one)*
  - `POST   /api/v1/organizations/{id}/suspend` → `SuspendOrganization` → `204 No Content`
    *(added beyond the original plan — exposes the already-implemented `SuspendOrganization`
    command; `422` with error code `organization.already-suspended` if already suspended)*
  - `POST   /api/v1/organizations/{id}/reactivate` → `ReactivateOrganization` → `204 No Content`
    *(added beyond the original plan, counterpart to suspend; `422` with error code
    `organization.already-active` if already active)*
- [x] `ProjectsController`
  - `GET    /api/v1/projects` → `GetProjectsFiltered` (paged, filterable, sortable)
  - `GET    /api/v1/projects/{id}` → `GetProjectById`
  - `GET    /api/v1/projects/{id}/tasks` → `GetProjectTasks` (paged)
  - `GET    /api/v1/projects/{id}/members` → `GetProjectMembers`
  - `GET    /api/v1/projects/{id}/boards` → list boards (from `ProjectDto`)
  - `POST   /api/v1/projects` → `CreateProject` → `201 Created`
  - `PUT    /api/v1/projects/{id}` → `UpdateProject` → `204 No Content`
  - `DELETE /api/v1/projects/{id}` → `DeleteProject` → `204 No Content`
  - `POST   /api/v1/projects/{id}/members` → `AddProjectMember` → `201 Created`
    *(handler is `ICommand` with no return value — controller re-queries `GetProjectMembers`
    after dispatch and picks the matching entry for the response body/`Location`)*
    *(no `404` path despite an earlier doc claiming one — `AddProjectMemberValidator`'s
    `ProjectId` rule does its own existence check via `MustAsync`, so a missing project fails
    validation (`422`) before the handler ever runs and could throw `NotFoundException`; found
    and fixed during Phase 9 test-writing review, same class of bug as `CreateBoardValidator`'s
    `ProjectId` rule and `CreateTaskValidator`'s `BoardId` rule)*
  - `DELETE /api/v1/projects/{id}/members/{userId}` → `RemoveProjectMember` → `204 No Content`
  - `POST   /api/v1/projects/{id}/archive` → `ArchiveProject` → `204 No Content`
    *(added beyond the original plan — exposes the already-implemented `ArchiveProject` command,
    mirroring `OrganizationsController`'s Suspend/Reactivate precedent)*
  - `POST   /api/v1/projects/{id}/unarchive` → `UnarchiveProject` → `204 No Content`
    *(added beyond the original plan, counterpart to archive)*
  - `PUT    /api/v1/projects/{id}/members/{userId}/role` → `ChangeMemberRole` → `204 No Content`
    *(added beyond the original plan — exposes the already-implemented `ChangeMemberRole` command)*
  - Request DTOs live in `Ordinis.Api/Projects/Requests/` (`CreateProjectRequest`,
    `UpdateProjectRequest`, `AddProjectMemberRequest`, `ChangeMemberRoleRequest`), mirroring
    `Organizations/Requests/`
  - Verified end-to-end against a real local SQL Server database (create → get → rename →
    add/change-role/remove member → archive/unarchive → delete → `404` on subsequent get →
    `422` on validation failure)
- [x] `BoardsController`
  - `GET    /api/v1/boards/{id}` → `GetBoardById`
  - `GET    /api/v1/boards/{id}/tasks` → `GetBoardTasks` (paged)
  - `POST   /api/v1/projects/{id}/boards` → `CreateBoard` → `201 Created`
    *(only action on this controller whose route doesn't share the controller's own
    `api/v1/boards` prefix — uses ASP.NET Core's absolute-route override (`[HttpPost("/api/v1/projects/{projectId:guid}/boards")]`,
    leading `/`) so the create-via-parent URI still lives on `BoardsController`; the response's
    `Location` header still points at the board's own canonical `GetById` route, not the nested
    creation path)*
    *(no `404` path despite an earlier doc claiming one — `CreateBoardValidator`'s `ProjectId`
    rule does its own existence-and-not-archived check via `MustAsync`, so a missing or archived
    project fails validation (`422`) before the handler ever runs and could throw
    `NotFoundException`; found and fixed during Phase 9 test-writing review, same class of bug as
    `CreateTaskValidator`'s `BoardId` rule)*
  - `PUT    /api/v1/boards/{id}/name` → `RenameBoard` → `204 No Content`
  - `POST   /api/v1/boards/{id}/archive` → `ArchiveBoard` → `204 No Content`
    *(`422` with error code `board.already-archived` if already archived)*
  - `POST   /api/v1/boards/{id}/unarchive` → `UnarchiveBoard` → `204 No Content`
    *(added beyond the original plan, counterpart to archive — see Phase 4 Step 2 note; `422`
    with error code `board.not-archived` if not archived)*
  - Request DTOs live in `Ordinis.Api/Projects/Requests/` (`CreateBoardRequest`, `RenameBoardRequest`)
  - **Found during review** (controller was drafted ahead of a formal pass): `GetById`/`GetTasks`
    routes were missing the `:guid` route constraint used everywhere else in the codebase;
    `RenameBoard`'s XML doc claimed a `400` response for invalid input, but this project always
    returns `422` for validation failures (no `[ProducesResponseType(400)]` was ever declared
    either — the doc comment just didn't match reality); `ArchiveBoard` was missing its `422`
    response type/doc for the already-archived domain guard. All fixed; see the Application-layer
    bug fixes noted in Phase 4 Step 2 above (`CreateBoardValidator`, `RenameBoardHandler`).
  - Verified end-to-end against a real local SQL Server database (create via the nested project
    route with a correct `Location` header → get → rename → create with a nonexistent
    `CreatedByUserId` now returns `422` instead of `500` → archive → `422` on double-archive →
    rename-while-archived `422` → unarchive → `422` on double-unarchive)
- [x] `TasksController`
  - `GET    /api/v1/tasks` → `GetTasksFiltered` (paged, filterable by assignee/status/priority/board, sortable)
  - `GET    /api/v1/tasks/{id}` → `GetTaskById`
  - `GET    /api/v1/tasks/{id}/comments` → comments from `TaskDto` (no separate query needed)
  - `GET    /api/v1/tasks/{id}/attachments` → attachments from `TaskDto`
  - `POST   /api/v1/tasks` → `CreateTask` → `201 Created`
    *(no `404` path despite an earlier doc claiming one — `CreateTaskValidator`'s `BoardId` rule
    does its own existence check via `MustAsync`, so a missing board fails validation (`422`)
    before the handler ever runs and could throw `NotFoundException`; found and fixed during
    Phase 9 test-writing review)*
  - `PUT    /api/v1/tasks/{id}` → `UpdateTask` → `204 No Content`
  - `DELETE /api/v1/tasks/{id}` → `DeleteTask` → `204 No Content`
    *(`requestedByUserId` passed as a query parameter, not a request body — consistent with
    HTTP DELETE conventions and matching this controller's own established pattern; the
    now-unused `DeleteTaskRequest` record was removed as dead code)*
  - `POST   /api/v1/tasks/{id}/comments` → `AddComment` → `201 Created`
  - `PUT    /api/v1/tasks/{id}/comments/{commentId}` → `EditComment` → `204 No Content`
    *(no `404` path despite an earlier doc claiming one — `EditCommentValidator`'s combined
    task/comment/author-ownership `MustAsync` check intercepts any missing task or comment with
    `422` before the handler runs; found and fixed during Phase 9 test-writing review)*
  - `DELETE /api/v1/tasks/{id}/comments/{commentId}` → `RemoveComment` → `204 No Content`
    *(`422` with error code `task.comment-not-found` if the comment doesn't exist — the handler
    lets `ProjectTask.RemoveComment()`'s `DomainException` surface directly)*
  - `POST   /api/v1/tasks/{id}/attachments` → `AddAttachment` → `201 Created`
    *(`multipart/form-data`, not JSON — `[FromForm] IFormFile file` + `[FromForm] Guid
    uploadedByUserId` bound directly as action parameters rather than wrapped in a request
    record, since `IFormFile` binds unreliably inside a bound complex type)*
  - `DELETE /api/v1/tasks/{id}/attachments/{attachmentId}` → `RemoveAttachment` → `204 No Content`
    *(`404`, not `422`, if the attachment doesn't exist — unlike `RemoveComment`, this handler
    pre-checks attachment existence itself and throws `NotFoundException` before ever reaching
    `ProjectTask.RemoveAttachment()`'s `DomainException` path; verified live rather than assumed
    from the domain method's XML doc, which describes the unreachable path)*
  - Request DTOs live in `Ordinis.Api/Tasks/Requests/` (`CreateTaskRequest`, `UpdateTaskRequest`,
    `AddCommentRequest`, `EditCommentRequest`) — all four now carry XML doc comments; previously
    only `CreateUserRequest`-style requests elsewhere had them, this controller's had none
  - **Found during review** (controller was drafted ahead of a formal pass, same as
    `UsersController`/`BoardsController`): `CreateTaskValidator.RequestedByUserId` and
    `AddAttachmentValidator.UploadedByUserId` both had no existence check (only `.NotEmpty()`) —
    the same bug shape found and fixed three times already this phase (`CreateProjectValidator`,
    `CreateBoardValidator`). `ProjectTask.ReporterId` and `Attachment.UploadedByUserId` are both
    required FKs with `DeleteBehavior.Restrict`, so a nonexistent ID passed validation and blew up
    as an unhandled `500` at `SaveChangesAsync` instead of a clean `422`. Fixed both with the same
    `MustAsync` existence check pattern; `AddAttachmentValidator` needed a new `IAppDbContext db`
    constructor parameter to support it.
  - Verified end-to-end against a real local SQL Server database, including a real
    `multipart/form-data` file upload/download round-trip through `LocalFileStorageService`
    (create task → nonexistent `RequestedByUserId` now `422` not `500` → comment add/edit/remove
    → attachment upload → nonexistent `UploadedByUserId` now `422` not `500` → download the
    uploaded file → remove attachment → confirm file deleted from disk → delete task → `404`)
- [x] `UsersController`
  - `GET    /api/v1/users/{id}` → `GetUserById`
  - `GET    /api/v1/users/{id}/tasks` → `GetUserTasks` (paged)
  - `POST   /api/v1/users` → `CreateUser` → `201 Created`
  - `PUT    /api/v1/users/{id}` → `UpdateUser` → `204 No Content`
  - `PUT    /api/v1/users/{id}/org-role` → `ChangeUserOrgRole` → `204 No Content`
    *(added beyond the original plan — exposes the already-implemented `ChangeUserOrgRole`
    command; `PUT` rather than `PATCH` — `/org-role` is treated as its own addressable single-value
    sub-resource being fully replaced, matching `ProjectsController`'s identical-shape precedent
    `PUT .../members/{userId}/role`)*
  - `POST   /api/v1/users/{id}/deactivate` → `DeactivateUser` → `204 No Content`
    *(added beyond the original plan, mirroring `OrganizationsController`'s Suspend/Reactivate
    precedent; `422` with error code `user.already-inactive` if already inactive)*
  - `POST   /api/v1/users/{id}/reactivate` → `ReactivateUser` → `204 No Content`
    *(added beyond the original plan, counterpart to deactivate; `422` with error code
    `user.already-active` if already active)*
  - Request DTOs live in `Ordinis.Api/Users/Requests/` (`CreateUserRequest`, `UpdateUserRequest`,
    `ChangeUserOrgRoleRequest`, `DeactivateUserRequest`, `ReactivateUserRequest`)
  - **Found during review** (controller was drafted ahead of a formal pass): `CreateUser` returned
    the bare `Guid` id as the `201` body instead of re-querying `GetUserById` and returning the
    full `UserDto` — the declared `[ProducesResponseType(typeof(UserDto), 201)]` didn't match what
    was actually serialized. Fixed to match `OrganizationsController`/`ProjectsController`'s
    create-then-requery pattern. Also fixed: four of five request DTOs were missing their
    `namespace` declaration entirely (silently compiled into the global namespace); `UpdateUser`/
    `ChangeOrgRole` were missing `409` (both handlers catch `DbUpdateConcurrencyException`);
    `ChangeOrgRole`/`Deactivate`/`Reactivate` were missing `422`; `GetTasks` had no
    `[ProducesResponseType]` at all; `DeactivateUser`/`ReactivateUser` had no validator (only
    `.NotEmpty()` on `RequestedByUserId`, added to match `UpdateUser`/`ChangeUserOrgRole`) — new
    validator tests added in `tests/Ordinis.UnitTests/Application/Users/Validators/`
    (`DeactivateUserValidatorTests`, `ReactivateUserValidatorTests`).
  - Verified end-to-end against a real local SQL Server database (create → get → deactivate →
    `422` on double-deactivate → reactivate → change role → rename → list tasks → `404` on a
    missing user)

### Minimal API endpoints
- [x] `SearchEndpoints` (`/api/v1/search?q=&type=tasks|projects`) — delegates to `GetTasksFiltered` / `GetProjectsFiltered` with text search param
  - `src/Ordinis.Api/MinimalApis/SearchEndpoints.cs` — `MapSearchEndpoints` extension method,
    mapped in `Program.cs` alongside `MapControllers()`; resolves `IDispatcher` the same way
    controllers do (per `IDispatcher`'s own doc comment: "Controllers and Minimal API endpoints
    depend on this interface, not on individual handlers directly")
  - `TaskFilter`/`ProjectFilter` (`GetTasksFiltered.cs`/`GetProjectsFiltered.cs`) gained a new
    `Search` member, matched case-insensitively against `Title`/`Description` (tasks) or
    `Name`/`Description` (projects); `q` is required and `type` must be `tasks` or `projects`
    (matched case-insensitively) or the endpoint returns `400`
  - Missing `q`/invalid `type` responses and the `200` body shape (bare array + `X-Total-Count`
    header, matching every other paged list endpoint) all go through the same
    `ProblemDetailsFactory`/response conventions used by the controllers
  - **Found during code review:** the initial `Search` implementation used
    `string.Contains(search, StringComparison.OrdinalIgnoreCase)` — an overload EF Core's SQL
    Server/Npgsql providers cannot translate to SQL, which would have thrown at request time
    against a real database while passing silently in unit tests (which run on the EF Core
    InMemory provider). Fixed to the same `.ToLower().Contains(...)` idiom already used in
    `CreateBoard.cs`/`RenameBoard.cs`. Also fixed: case-sensitive `type` comparison, a
    `.Produces<PagedResult<T>>` OpenAPI annotation that didn't match the actual bare-array
    response body, and error responses that bypassed `ProblemDetailsFactory` (silently omitting
    the `correlationId` extension every other error response carries)
  - Unit tests added for the `Search` filter (match on each searchable field, case-insensitivity,
    no-match, whitespace-only) in `GetTasksFilteredHandlerTests.cs`/`GetProjectsFilteredHandlerTests.cs`
  - No integration test for the endpoint itself — `Ordinis.IntegrationTests` has no test files or
    `WebApplicationFactory` scaffolding yet; standing that up is out of scope here
- [x] Auth endpoints scaffolded as placeholder (`/auth/login`, `/auth/refresh`) — fully implemented in Phase 8
  - `src/Ordinis.Api/MinimalApis/AuthEndpoints.cs` — both routes return `501 Not Implemented` via
    `ProblemDetailsFactory`; no request/response DTOs or credential logic yet, deliberately, since
    JWT + refresh token issuance is Phase 8's job

### Cross-cutting concerns on all endpoints
- [x] All list endpoints: filtering, sorting, pagination via query string; return `X-Total-Count` header
  - Verified across all controllers: `OrganizationsController.GetProjects`, `ProjectsController.GetProjects`/`GetTasks`,
    `BoardsController.GetTasks`, `TasksController.GetTasks`, `UsersController.GetTasks`, and `SearchEndpoints` all
    accept filter/sort/page/pageSize query params and set `X-Total-Count` to the unpaged total.
  - `GET /projects/{id}/members`, `GET /projects/{id}/boards`, `GET /tasks/{id}/comments`, `GET /tasks/{id}/attachments`
    are deliberately unpaged — bounded, embedded child collections (owned entities capped by
    `ProjectDto.MaxEmbeddedCollectionSize`-style limits), not independently queryable resources
  - **Found during Phase 9 test-writing review:** SQL gives no guaranteed row order for ties on
    the primary sort key, so every paginated/capped/embedded list query risked nondeterministic
    results whenever two rows tied (e.g. identical `Name`, or `CreatedAt` colliding — seeding
    multiple rows in one `SaveChangesAsync` call stamps them all with the exact same `CreatedAt`).
    Fixed by adding `EntityQueryableExtensions.ThenByStableId<T>()`
    (`Ordinis.Application.Common`, `where T : Entity`) and applying it at every
    `OrderBy`/`OrderByDescending` call site that lacked a tiebreaker — 6 paginated query handlers,
    1 capped embedded-list query, 1 unbounded list query, and 2 DTO-embedding mappers. Documented
    as a hard convention in `CLAUDE.md`.
- [x] All list endpoints: sparse fields support via `?fields=` query string; mapper respects field list
  - `Ordinis.Api/Common/DataShaping/DataShaper.cs` — reflection-based shaping, applied at the API
    layer (not in the Application-layer mappers, which stay untouched manual DTO mappers). Comma-
    separated, case-insensitive field names; unknown names ignored; `Id` always included so shaped
    resources stay addressable. Wired into every unbounded list endpoint (same set as the
    pagination item above) plus `SearchEndpoints`.
  - **Two bugs found during manual verification:** (1) `ShapeData<T>(IEnumerable<T>, ...)` and
    `ShapeData<T>(T, ...)` were ambiguous overloads — calling `ShapeData(result.Items, fields)` with
    `result.Items: IReadOnlyList<TaskSummaryDto>` bound to the *single-item* overload (identity
    conversion beats the interface conversion needed for the collection overload), reflecting the
    list itself and throwing `TargetParameterCountException` on its `this[int]` indexer. Renamed to
    `ShapeCollection<T>`/`ShapeItem<T>` to remove the ambiguity. (2) Shaped responses serialized
    `ExpandoObject` keys in PascalCase (`PropertyInfo.Name`), while every other endpoint's DTOs
    serialize camelCase via MVC's default `System.Text.Json` casing — shaped and unshaped responses
    were inconsistently cased. Fixed by running each key through
    `JsonNamingPolicy.CamelCase.ConvertName`.
  - Verified end-to-end against a real local SQL Server database: full response with no `fields`,
    `fields=title,status` returning only those two plus `id`, an unknown field name being silently
    dropped while `id` still appears, and camelCase keys matching normal DTO responses.
- [x] All endpoints return Problem Details on error (enforced by `GlobalExceptionMiddleware`)
  - **Gap found:** `GlobalExceptionMiddleware` only translates exceptions thrown *during* the
    pipeline. `[ApiController]`'s automatic model-state validation (malformed JSON, a route/query
    value that can't convert to its target type, a missing required field) short-circuits *before*
    the action runs and never throws — it returned ASP.NET Core's own default
    `ValidationProblemDetails`, bypassing `ProblemDetailsFactory` entirely (no `correlationId`, a
    generic RFC 9110 `type` URI instead of this API's `https://httpstatuses.io/{status}`
    convention). Fixed via `ConfigureApiBehaviorOptions().InvalidModelStateResponseFactory` in
    `ApiServiceExtensions`, routed through a new `ProblemDetailsFactory.CreateModelBindingValidation`
    (`400`, kept distinct from FluentValidation's `422`: unbindable request vs. bindable-but-invalid
    request).
  - **Bug found:** a Minimal API query parameter that fails to bind (e.g. `GET /search?page=abc`)
    throws `BadHttpRequestException`, which `GlobalExceptionMiddleware`'s catch-all was turning into
    a `500` instead of the correct `400`. Fixed by adding a dedicated
    `catch (BadHttpRequestException ex)` (before the catch-all) that maps to `ex.StatusCode`.
  - **Gap found:** an unmatched route (`404`) or a disallowed HTTP method (`405`) reaches the client
    with an empty body — no exception is ever thrown, so `GlobalExceptionMiddleware` never runs.
    Added `app.UseStatusCodePages(...)` in `Program.cs` (after `GlobalExceptionMiddleware`, before
    routing) to give these a Problem Details body via the same `ProblemDetailsFactory.Create`; it
    only fires when the response body is still empty, so it never touches the exception-driven `404`
    that `NotFoundException` already produces with its own body.
  - Verified end-to-end against a real local SQL Server database: malformed enum value, malformed
    JSON body, non-guid route segment, unmatched route, disallowed method, and unparseable Minimal
    API query parameter all now return a consistent Problem Details body with `correlationId` and
    the standard `type` URI; confirmed no regression on the FluentValidation `422` path, the
    `DomainException` → `urn:ordinis:error:{code}` path, and the happy path.
- [x] All `POST` endpoints: `201 Created` with `Location: /api/v1/{resource}/{id}` header
  - Audited every `[HttpPost]` action across all five controllers plus `AuthEndpoints`. They split
    into two groups by design, both already correct:
    - **Resource-creation POSTs** (`CreateOrganization`, `CreateUser`, `CreateProject`,
      `CreateBoard`, `CreateTask`, `AddComment`, `AddAttachment`, `AddProjectMember`) all return
      `CreatedAtAction(...)` → `201` with a `Location` header. Top-level resources point at their
      own canonical `GetById` (including `BoardsController.Create`, whose *creation* route is nested
      under `/api/v1/projects/{projectId}/boards` but whose `Location` still resolves to
      `/api/v1/boards/{id}`, per the design already noted earlier in this phase). Sub-resources with
      no dedicated single-item `GET` (comments, attachments, project members) point at their
      parent's list endpoint instead (`GetComments`, `GetAttachments`, `GetMembers`) — an established
      precedent from when those controllers were first built, not a new decision.
    - **Action/state-transition POSTs** (`Suspend`/`Reactivate`, `Archive`/`Unarchive`,
      `Deactivate`/`Reactivate`) correctly return `204 No Content` instead — they don't create a new
      resource, so a `Location` header wouldn't have anywhere meaningful to point. The checklist
      wording is a simplification; per-endpoint status codes already follow REST convention.
    - `/auth/login` and `/auth/refresh` are `POST` but return `501 Not Implemented` placeholders —
      out of scope until Phase 8.
  - Verified end-to-end against a real local SQL Server database: `Location` header value and `201`
    status for organization/user/project/board/task/comment/attachment/member creation.
- [x] All `PUT` / `DELETE` endpoints: `204 No Content` on success
  - Audited all 13 `[HttpPut]`/`[HttpDelete]` actions across all five controllers — every one maps
    1:1 to a `return NoContent();`, confirmed by counting: 21 total `NoContent()` returns in the API
    project = 13 `PUT`/`DELETE` actions + 8 action-style `POST`s (suspend/reactivate/archive/
    unarchive/deactivate, already covered by the `201 Created` item above), with none left over.
  - Verified end-to-end against a real local SQL Server database: `204` confirmed live for organization
    rename, user rename, board rename, task update, task delete, and project delete.
- [x] XML doc comments on all controller actions — used by OpenAPI in Phase 10
  - Audited every public action across all five controllers (`<summary>`, a `<param>` for every
    parameter including `CancellationToken`, and a `<response code="...">` for every status code
    declared via `[ProducesResponseType]`) plus `SearchEndpoints`/`AuthEndpoints`'s
    `MapXEndpoints` methods — all already complete, no gaps.
  - **Gap found:** three request DTOs had no doc comments at all — `CreateBoardRequest`,
    `RenameBoardRequest`, `CreateUserRequest` — inconsistent with every sibling DTO in the same
    folders (`AddProjectMemberRequest`, `UpdateProjectRequest`, `UpdateUserRequest`, etc.), which all
    carry a `<summary>` naming the HTTP route plus a `<param>` per positional record member. Brought
    all three up to the same pattern.

**Git tag:** `v0.6-phase6-api-core`

---

## Phase 7 — API layer: advanced REST features

> ⚠️ Requires Phase 6.

### State transitions
- [x] `POST /api/v1/tasks/{id}/move` → `MoveTask` — body: `{ "status": "InProgress", "requestedByUserId": "..." }` (done — `src/Ordinis.Api/Tasks/TasksController.cs` `Move`; the command/handler/validator already existed from Phase 4/9, this phase only wired the HTTP endpoint)
- [x] `POST /api/v1/tasks/{id}/assign` → `AssignTask` — body: `{ "assigneeId": "...", "requestedByUserId": "..." }` (done — `TasksController.Assign`)
- [x] `POST /api/v1/tasks/{id}/unassign` → `UnassignTask` — body: `{ "requestedByUserId": "..." }` (done — `TasksController.Unassign`)
- [x] `POST /api/v1/tasks/{id}/close` → `MoveTask` with `status: Done` (convenience alias) (done — `TasksController.Close`. **Design deviation from this checklist, flagged and agreed before implementing:** the checklist named a `Closed` status that doesn't exist in `ProjectTaskStatus` — the domain only has `Done`/`Cancelled` as terminal states. `close` aliases to `Done`, the closest existing equivalent)
- [x] `POST /api/v1/tasks/{id}/reopen` → `MoveTask` with `status: ToDo` (done — `TasksController.Reopen`. **Second deviation, same discussion:** `Done` and `Cancelled` previously had zero outbound transitions (`AllowedTransitions[Done] = {}`), so `reopen` as originally specified could never succeed. Extended the state machine with a single `Done -> ToDo` transition (`ProjectTaskStatusExtensions.AllowedTransitions`); `Cancelled` remains fully terminal — cancelling is meant to be final, unlike closing. To avoid silently loosening `Update`/`Assign`/`Unassign`, which gate on `EnsureNotTerminal()` → `IsTerminal()`, `IsTerminal()` was changed from `AllowedTransitions[status].Count == 0` (derived) to an explicit `status is Done or Cancelled` check, decoupled from the transition adjacency list — so `Done` still blocks edits/(re)assignment until explicitly reopened via `Move`, it just now has exactly one legal way out. Updated `ProjectTaskStatusExtensionsTests`/`ProjectTaskTests` accordingly; added `Move_FromDoneToToDo_Reopens`/`Move_FromDoneToAnyOtherStatus_ThrowsDomainException`)

### HATEOAS

See [docs/REST_API_CONCEPTS.md#hateoas](docs/REST_API_CONCEPTS.md#hateoas) for the concept writeup (why this exists as a REST pattern, not just what the code does).

- [x] Add `HateoasLink` record — `{ Rel, Href, Method }` (`Ordinis.Application.Common.HateoasLink`), embedded in DTOs as a `Links` property serialized to JSON as `_links` via `[JsonPropertyName("_links")]` (done — named `HateoasLink`, not `HateoasLinks` as the checklist originally said, since it's one link per list entry, not a wrapper)
- [x] `TaskDto` gets `_links`: `self`, `assign`, `delete` (always present), plus `move` and one `move:{status}` link per legal transition (driven by `ProjectTaskStatusExtensions.GetAllowedTransitions()` — the checklist named a `GetValidTransitions()` method that doesn't exist; `GetAllowedTransitions` is the actual name) when the task has any outbound transition. `Cancelled` tasks (no outbound transitions) get no `move`/`move:*` links, since advertising an action that would always 422 isn't useful. Built in `TaskMapper.ToDto` only — `TaskSummaryDto` (list view) has no links. Relative hrefs (e.g. `/api/v1/tasks/{id}`) are hardcoded strings matching `TasksController`'s routes — no `IUrlHelper`/`LinkGenerator` dependency, keeping `TaskMapper` a pure function with no ASP.NET Core coupling
- [x] `ProjectDto` gets `_links`: `self`, `tasks`, `boards`, `members`, `delete` — built in `ProjectMapper.ToDto`, same relative-href approach as `TaskDto`

### Optimistic concurrency
- [x] `TaskDto.ConcurrencyToken` (already defined in Phase 4) returned as `ETag` response header on `GET /tasks/{id}` (done — `TasksController.GetById`. **Scope widened, agreed before implementing:** `ProjectDto`/`BoardDto` didn't have a `ConcurrencyToken` field yet even though `Project`/`Board` also carry a `RowVersion` (Phase 2) — added it to both DTOs (`ProjectMapper.ToDto`/`ToBoardDto`) and the same `ETag` header to `ProjectsController.GetById`/`BoardsController.GetById`, so all three `RowVersion`-bearing aggregates get identical treatment instead of Tasks-only)
- [x] `PUT /tasks/{id}` and state transition endpoints require `If-Match` header — middleware extracts and passes to handler (done, and — same scope-widening decision — extended to every mutating endpoint on `Project` and `Board` too: `PUT`/archive/unarchive/delete on `Project` and `Board`, plus `POST`/`PUT`/`DELETE` on project members, since those bump `Project.RowVersion` under the hood. `DELETE /tasks/{id}` was folded in as well, even though the checklist wording only names `PUT` and state transitions. Comment/attachment endpoints are deliberately **not** guarded — `EditCommentHandler`'s own remarks already document an intentional last-write-wins trade-off there, predating this phase; extending `If-Match` to them was considered and rejected as disproportionate to the risk. See `docs/CONCURRENCY.md` for the full guarded/not-guarded table and rationale)
- [x] Handler compares incoming token to current `RowVersion`; mismatch → `ConcurrencyException` → `409 Conflict` (done via a new `ConcurrencyGuard.EnsureMatch` helper (`Ordinis.Application.Common`), called by every guarded handler immediately after loading the entity — after the existence check, so a nonexistent resource still 404s rather than being masked by a concurrency error. This is a *proactive* check, run before `SaveChangesAsync`; the existing reactive `catch (DbUpdateConcurrencyException)` around each handler's `SaveChangesAsync` call is untouched and stays as defense-in-depth for a same-request write race. Both paths now throw `ConcurrencyException`, via a new no-inner-exception constructor overload added for the proactive case. Missing `If-Match` itself is enforced separately, one level up, via a `NotNull()` FluentValidation rule on each command's new `IfMatch` property — reuses the existing "all command rules run centrally through `IValidator<T>`" pipeline rather than adding a new enforcement mechanism, so it surfaces as this API's standard `422` rather than a bespoke status code)
- [x] `ConcurrencyTokenMiddleware` — reads `If-Match`, decodes Base64 → `byte[]`, attaches to `HttpContext.Items` for handlers to consume (done — `src/Ordinis.Api/Common/ConcurrencyTokenMiddleware.cs`; strips a `W/` weak-validator prefix and surrounding quotes before decoding. Deliberately a "dumb" extractor with no per-endpoint enforcement logic of its own — a missing or malformed header both just leave the `HttpContext.Items` entry unset, and the `NotNull()` validator rule above is what actually requires it per guarded command. Registered in `Program.cs` right after `GlobalExceptionMiddleware`. Full pipeline writeup: `docs/API_INFRASTRUCTURE.md#concurrencytokenmiddleware`; full ETag/If-Match mechanism writeup: `docs/CONCURRENCY.md`)

**Found during implementation:** `RowVersion` is app-managed, not database-generated — `AppDbContext.SaveChangesAsync` assigns a fresh `Guid.CreateVersion7().ToByteArray()` to every tracked `Added`/`Modified` `AggregateRoot`, since SQL Server's native auto-incrementing `rowversion` column type has no PostgreSQL equivalent and this project runs identically on both providers. `.IsConcurrencyToken()` in each entity's `IEntityTypeConfiguration<T>` only tells EF Core to include the property in the `WHERE` clause and compare rows-affected — it doesn't require the value itself to be database-generated, so this doesn't weaken the guarantee at all. (This predates Phase 7 — already the case since Phase 2/5 — but is documented here since it's exactly the mechanism the ETag/If-Match feature builds on top of, and initial documentation for this phase had it backwards.)

**Test fallout, expected and fixed:** making `If-Match` mandatory broke ~40 existing integration tests across `TasksControllerTests`/`ProjectsControllerTests`/`BoardsControllerTests` that called guarded endpoints without the header (404-for-nonexistent-entity tests got `422` instead, since validation now runs — and fails — before the handler's own existence check; `*_ConcurrentModification_Returns409` race tests timed out, since both racing requests were rejected by validation before the race-orchestration logic ever ran). Fixed by adding `GetTaskRowVersionAsync`/`GetProjectRowVersionAsync`/`GetBoardRowVersionAsync` and a header-aware `SendAsync` helper to `IntegrationTestBase`, and updating every affected call site to supply either the entity's current `RowVersion` (success-path tests) or a fixed non-null placeholder token (404-for-nonexistent-entity tests, where any well-formed `If-Match` is enough to clear validation and let the real 404 fire). Added a small number of new tests per controller for the missing-header (`422`) and stale-token (`409`) cases plus the `GET`-returns-`ETag` case. Final counts: 608/608 unit tests, 208/208 integration tests.

### Idempotency
- [x] `IdempotencyMiddleware` — reads `Idempotency-Key` header on `POST` requests; caches response by key (in-memory, 24h TTL); replays cached response on duplicate key (done — `src/Ordinis.Api/Common/IdempotencyMiddleware.cs`, registered right after `ConcurrencyTokenMiddleware`. See `docs/API_INFRASTRUCTURE.md#idempotencymiddleware` for the pipeline placement and `docs/IDEMPOTENCY.md` for the full end-to-end mechanism writeup, mirroring `docs/CONCURRENCY.md`'s ETag/If-Match doc)
- [x] Applied to: `POST /tasks`, `POST /tasks/{id}/comments`, `POST /tasks/{id}/attachments`, `POST /projects`, `POST /projects/{id}/members`, `POST /projects/{id}/boards`, `POST /organizations`, `POST /users` (done — each action marked with a new `[Idempotent]` marker attribute, chosen over a hardcoded route allowlist in the middleware so it survives route changes and self-documents at the call site, same idiom as `[Consumes]`/`[ProducesResponseType]`)

**Design decisions, agreed before implementing:**
- **Store abstraction.** `IIdempotencyStore` (`Ordinis.Application/Common/IIdempotencyStore.cs`) is a pure contract with one implementation today, `InMemoryIdempotencyStore` (`Ordinis.Infrastructure/Common/`, wraps `IMemoryCache`) — mirrors the existing `IFileStorageService` swap-friendly pattern, so a Redis-backed store for multi-instance deployments can replace it later without touching the middleware or any controller.
- **Reused-key conflict detection.** The plan's wording only says "replays cached response on duplicate key," but a bare key match doesn't guard against a client reusing the same key for a genuinely different request. The cached record also carries a SHA-256 hash of the original request body; a replay with a matching hash returns the cached response, a mismatch throws the new `IdempotencyKeyConflictException` → `409 Conflict` instead of silently returning the wrong cached response. Hashing raw bytes (not decoded text) matters here specifically because `AddAttachment`'s body is multipart/binary — a decode-then-re-encode round trip through UTF-8 would silently corrupt invalid byte sequences before hashing.
- **Only `2xx` responses are cached.** A failed request (`422` validation failure, `404`, `500`, ...) is never cached, so the client can retry the same key once the request is fixed. This also avoids a subtler bug: `ProblemDetailsFactory` stamps the *originating* request's `correlationId` into every error body, so replaying a cached error verbatim on a retry would show a stale, misleading correlation ID.
- **Attribute-based opt-in over a route allowlist.** `[Idempotent]` (`Ordinis.Api/Common/IdempotentAttribute.cs`) marks exactly the eight in-scope actions. The middleware reads it via `context.GetEndpoint()`, which is already resolved by the time any custom `app.UseMiddleware<>()` runs — confirmed during implementation that WebApplication's minimal hosting model auto-inserts `UseRouting()` as the very first pipeline entry whenever endpoints are mapped, ahead of every custom middleware in `Program.cs`, so no explicit `UseRouting()` call was needed to make this work.
- **`MemoryCacheOptions` has no `TimeProvider`-based clock override** in the referenced `Microsoft.Extensions.Caching.Memory` package version (10.0.9) — attempted during implementation, reverted when it failed to compile (`MemoryCacheOptions` has no `TimeProvider` member). Cache expiry runs on the wall clock, the one clock dependency in this codebase not routed through the app's `TimeProvider` singleton.

### API versioning
- [x] Add URL-segment versioning — `/api/v1/` prefix on all existing routes (done — found already in place: every resource controller (`TasksController`, `ProjectsController`, `BoardsController`, `OrganizationsController`, `UsersController`) has hardcoded `[Route("api/v1/...")]` since Phase 6, and `SearchEndpoints`'s Minimal API route is `/api/v1/search`. This item was never explicitly tracked as done at the time; no code change was needed here, just checking it off. `AuthEndpoints` (`/auth/login`, `/auth/refresh`) is deliberately left unversioned — CLAUDE.md's API style section calls out auth as a focused non-resource route, and Phase 8 already plans it as a public/unauthenticated endpoint, so it doesn't belong under `/api/v{n}` with the other resources)
- [x] Add `/api/v2/tasks` as a demonstration endpoint — returns `TaskDto` with an additional `_links` field (v2 difference) to show versioning in practice (done — `src/Ordinis.Api/Tasks/TasksV2Controller.cs`, `[Route("api/v2/tasks")]`, single `GetById` action. **Design decisions, agreed before implementing:** no API versioning NuGet package (e.g. `Asp.Versioning.Mvc`) was introduced — kept the same hardcoded-literal-route pattern already used by every v1 controller, consistent with this project's preference for explicit code over framework abstraction (no MediatR, no AutoMapper) for what is a single demonstration endpoint. No new DTO type either: v2 dispatches the same `GetTaskById` query and reuses `TaskDto`/`TaskMapper` unchanged, then appends one extra `HateoasLink("board", "/api/v1/boards/{boardId}", "GET")` to the returned `Links` list — `TaskDto` carries `BoardId` but had no direct hyperlink to the parent board despite already exposing `self`/`assign`/`delete`/`move` links, so this is a genuine, useful v1/v2 diff rather than a contrived one. `TaskDto` is a plain class (not a record), so the extra link is applied by copying every field into a new `TaskDto` via an object initializer rather than a `with` expression. Covered by `tests/Ordinis.IntegrationTests/Tasks/TasksV2ControllerTests.cs`: v2 response contains every v1 link plus `board`, `ETag` header matches `ConcurrencyToken`, and 404 for a nonexistent task — plus confirms v1's response is unaffected (no `board` link there))

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
- [ ] **First production Dapper usage** (see Phase 5) — this is the first query with a real multi-table join (`OutboxMessages` → `Tasks`/`Boards` → `Project`) instead of a straightforward filtered `DbSet` query, so it's the query handler that gets `IAppDbContext.GetDbConnection()` instead of LINQ; write provider-aware paging SQL (SQL Server `OFFSET/FETCH` vs. PostgreSQL `LIMIT/OFFSET`), branching on the normalized provider string the same way `OutboxDispatcherJob` already does

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
- [x] `CreateTaskValidator` — `BoardId` required and exists (not archived); `Title` non-empty max 200 chars; `Priority` valid enum value (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/CreateTaskValidatorTests.cs`, using `FluentValidation.TestHelper`'s `TestValidateAsync` since the `BoardId`/`AssigneeId` rules are async `MustAsync` checks against the database; also covers the `AssigneeId` and `RequestedByUserId` rules. **Bug found later, during the `TasksController` review:** `RequestedByUserId` had no existence check at all (only `.NotEmpty()`) — same shape as `CreateProjectValidator`/`CreateBoardValidator`'s earlier fixes; `ProjectTask.ReporterId` is a required FK with `DeleteBehavior.Restrict`, so a nonexistent user ID blew up as an unhandled `500`. Fixed with the same `MustAsync` existence check; added `TestValidateAsync_RequestedByUserIdDoesNotExist_HasValidationErrorForRequestedByUserId` and `...Exists_HasNoValidationErrorForRequestedByUserId`)
- [x] `UpdateTaskValidator` — `Title` non-empty max 200 chars; `Priority` valid enum value (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/UpdateTaskValidatorTests.cs`; also covers `TaskId`/`RequestedByUserId` and a valid-command no-errors baseline)
- [x] `MoveTaskValidator` — `NewStatus` is a valid `ProjectTaskStatus` enum value (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/MoveTaskValidatorTests.cs`; also covers `TaskId`)
- [x] `AssignTaskValidator` — `AssigneeId` required; user exists and is a project member (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/AssignTaskValidatorTests.cs`; the validator itself only checks user existence — project-membership is deliberately deferred to the authorization layer per its own remarks, so the tests don't cover that; also covers `TaskId`/`RequestedByUserId`)
- [x] `AddCommentValidator` — `Content` non-empty, max 10 000 chars (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/AddCommentValidatorTests.cs`; also covers `TaskId` and the 10,000-char boundary; no `AuthorId` rule exists in the validator, so none is tested)
- [x] `EditCommentValidator` — `Content` non-empty, max 10 000 chars; requesting user is the comment author (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/EditCommentValidatorTests.cs`; also covers `TaskId`/`CommentId`/`RequestedByUserId` and the 10,000-char boundary)
- [x] `AddAttachmentValidator` — `FileName` non-empty; `SizeInBytes` > 0; `ContentType` non-empty (done — `tests/Ordinis.UnitTests/Application/Tasks/Validators/AddAttachmentValidatorTests.cs`; also covers `TaskId`/`FileStream`/`UploadedByUserId` and the `FileName`/`ContentType` length boundaries. **Bug found later, during the `TasksController` review:** `UploadedByUserId` had no existence check (only `.NotEmpty()`), same shape as the other `CreatedByUserId`/`RequestedByUserId` bugs this phase; `Attachment.UploadedByUserId` is a required FK with `DeleteBehavior.Restrict`. Fixed by adding an `IAppDbContext db` constructor parameter (the validator previously took none) plus the same `MustAsync` existence check; added `TestValidateAsync_UploadedByUserIdDoesNotExist_HasValidationErrorForUploadedByUserId` and `...Exists_HasNoValidationErrorForUploadedByUserId`)

**Git tag (after Part 1):** `v0.9-part1-task-validators`

---

#### Part 2 — Project, Board, Organization, and User validator tests
> Reuses all infrastructure from Part 1. Mechanical — follows the same shape.

**Project & Board validators**
- [x] `CreateProjectValidator` — `OrganizationId` required and exists; `Name` non-empty max 100 chars; generated slug unique within the organization (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/CreateProjectValidatorTests.cs`; also covers `CreatedByUserId` and `Description` max length 1000; fixed a bug found while testing — the `Name` rule lacked `Cascade(CascadeMode.Stop)`, so an empty `Name` let the slug-uniqueness `MustAsync` run anyway and throw inside `SlugGenerator.Slugify` instead of failing validation cleanly. **Second bug found later, during manual API testing:** `CreatedByUserId` had no existence check at all (only `.NotEmpty()`) — since `Project.CreatedByUserId` is a required FK with `DeleteBehavior.Restrict`, a nonexistent user ID passed validation and then blew up as a raw `DbUpdateException` at `SaveChangesAsync`, surfacing as an unhandled `500` instead of a clean `422`. Fixed by adding the same `MustAsync` existence check `AddProjectMemberValidator` already does for its own `UserId`; added `TestValidateAsync_CreatedByUserIdDoesNotExist_HasValidationErrorForCreatedByUserId` and `...Exists_HasNoValidationErrorForCreatedByUserId`)
- [x] `AddProjectMemberValidator` — `UserId` exists; `Role` valid enum value; user not already a member (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/AddProjectMemberValidatorTests.cs`; also covers `ProjectId`; fixed a gap found while testing — the validator had no rule for `Role` at all, so an out-of-range value passed silently; added `RuleFor(x => x.Role).IsInEnum()`, matching `ChangeMemberRoleValidator`)
- [x] `ChangeMemberRoleValidator` — `Role` valid enum value (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/ChangeMemberRoleValidatorTests.cs`; also covers `ProjectId`/`UserId`; purely synchronous, no DB state needed; the unused `IAppDbContext db` constructor parameter flagged earlier has since been removed)
- [x] `CreateBoardValidator` — `Name` non-empty max 100 chars; project exists and is not archived; no duplicate board name within the project (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/CreateBoardValidatorTests.cs`; also covers `ProjectId`/`CreatedByUserId` and case-insensitive duplicate-name scoping per project. **Bug found later, during the `BoardsController` review:** `CreatedByUserId` had no existence check at all (only `.NotEmpty()`) — same shape as the `CreateProjectValidator` bug above; `Board.CreatedByUserId` is a required FK with `DeleteBehavior.Restrict`, so a nonexistent user ID passed validation and blew up as an unhandled `500` at `SaveChangesAsync`. Fixed with the same `MustAsync` existence check; added `TestValidateAsync_CreatedByUserIdDoesNotExist_HasValidationErrorForCreatedByUserId` and `...Exists_HasNoValidationErrorForCreatedByUserId`)
- [x] `RenameBoardValidator` — `Name` non-empty max 100 chars; no duplicate board name within the project (done — `tests/Ordinis.UnitTests/Application/Projects/Validators/RenameBoardValidatorTests.cs`; also covers `BoardId`, renaming to its own current name, and case-insensitive duplicate scoping per project; fixed a latent bug found while testing — the board-lookup `Select(b => b.ProjectId)` projected a non-nullable `Guid`, so `SingleOrDefaultAsync` returned `Guid.Empty` instead of `null` when the board didn't exist, leaving the intended `if (projectId is null)` branch dead; cast to `(Guid?)` in the `Select` so it behaves as written)

**Organization validators**
- [x] `CreateOrganizationValidator` — `Name` non-empty max 100 chars; generated slug globally unique (done — `tests/Ordinis.UnitTests/Application/Organizations/Validators/CreateOrganizationValidatorTests.cs`; also covers `Description` max length 1000; fixed the same `Cascade(CascadeMode.Stop)` gap as `CreateProjectValidator` — an empty `Name` let the slug-uniqueness `MustAsync` run anyway and throw inside `SlugGenerator.Slugify`. Audited all 19 validators in `Ordinis.Application` for the same pattern — `Slugify` is the only throwing call reached from a validator's `MustAsync`/`Must`, and it only appears in these two validators, so no other instances exist)
- [x] `RenameOrganizationValidator` — `Name` non-empty max 100 chars (done — `tests/Ordinis.UnitTests/Application/Organizations/Validators/RenameOrganizationValidatorTests.cs`; also covers `OrganizationId`; purely synchronous, no DB state needed; no bugs found)
- [x] `UpdateOrganizationDescriptionValidator` — `Description` max length (if constrained) (done — `tests/Ordinis.UnitTests/Application/Organizations/Validators/UpdateOrganizationDescriptionValidatorTests.cs`; also covers `OrganizationId`; purely synchronous, no DB state needed; no bugs found)

**User validators**
- [x] `CreateUserValidator` — `Email` valid format and unique within the organization; `DisplayName` non-empty max 100 chars; `OrganizationId` exists; `Password` min 8 chars (done — `tests/Ordinis.UnitTests/Application/Users/Validators/CreateUserValidatorTests.cs`; also covers `OrganizationId` suspended-org rejection, case-insensitive email uniqueness scoped per organization, and `OrgRole` enum validity; no bugs found)
- [x] `UpdateUserValidator` — `DisplayName` non-empty max 100 chars (done — `tests/Ordinis.UnitTests/Application/Users/Validators/UpdateUserValidatorTests.cs`; also covers `UserId`/`RequestedByUserId`; purely synchronous, no DB state needed; no bugs found)
- [x] `ChangeUserOrgRoleValidator` — `Role` valid enum value (done — `tests/Ordinis.UnitTests/Application/Users/Validators/ChangeUserOrgRoleValidatorTests.cs`; also covers `UserId`/`RequestedByUserId`; purely synchronous, no DB state needed; no bugs found)
- [x] `DeactivateUserValidator` — `UserId`/`RequestedByUserId` not empty (done — `tests/Ordinis.UnitTests/Application/Users/Validators/DeactivateUserValidatorTests.cs`; purely synchronous, no DB state needed; **added during the `UsersController` review** — the validator itself didn't exist yet, only the handler; `DeactivateUserHandlerTests` already covered the domain-guard/not-found paths)
- [x] `ReactivateUserValidator` — `UserId`/`RequestedByUserId` not empty (done — `tests/Ordinis.UnitTests/Application/Users/Validators/ReactivateUserValidatorTests.cs`; same shape and same review-driven addition as `DeactivateUserValidator`)

**Git tag (after Part 2):** `v0.9-part2-remaining-validators`

---

#### Part 3 — Mapper tests
> Pure function tests — no EF Core, no DI, no async. No shared infrastructure needed.

**TaskMapper**
- [x] `ToSummaryDto` — all fields map correctly; null `AssigneeId` maps to null `AssigneeName`; no nested collections (done — `tests/Ordinis.UnitTests/Application/Tasks/Dtos/TaskMapperTests.cs`; also covers comment/attachment counts excluding soft-deleted comments)
- [x] `ToDto` — embedded `CommentDto` list maps correctly; `IsEdited` flag derived from `Comment.IsEdited`; embedded `AttachmentDto` list maps correctly; `userLookup` resolves assignee and comment author display names; missing user ID in lookup maps gracefully (done — same file; comments/attachments are attached via `ProjectTask`'s own public API (`AddComment`/`RemoveComment`/`AddAttachment`) rather than constructed directly, keeping the tests pure-function with no EF Core/DI/async; also covers the unpersisted-task `ConcurrencyToken` empty-string guard and the asymmetry between `AssigneeDisplayName` falling back to `null` vs. `CommentDto.AuthorDisplayName` falling back to `string.Empty` for a missing lookup entry — both intentional in the mapper, not bugs)

**ProjectMapper**
- [x] `ToSummaryDto` — all fields map correctly; `boardCount` and `memberCount` parameters flow through (done — `tests/Ordinis.UnitTests/Application/Projects/Dtos/ProjectMapperTests.cs`; `MemberCount` is derived from `Project.Members.Count`, not a parameter, so coverage confirms it stays correct independent of the `boardCount` parameter rather than testing it as a pass-through)
- [x] `ToDto` — embedded `BoardSummaryDto[]` maps correctly with per-board `taskCount`; embedded `ProjectMemberDto[]` maps correctly; truncation flags set correctly when board/member counts exceed cap (done — same file; boards/members attached via `Board.Create`/`Project.AddMember` rather than constructed directly; `BoardsAreTruncated`/`MembersAreTruncated` are derived properties on `ProjectDto` itself (`Boards.Count < BoardCount`), so coverage exercises them by supplying `ProjectDto.MaxEmbeddedCollectionSize + 1` boards/members rather than asserting a mapper-set flag; also covers the per-board `taskCount` lookup miss defaulting to `0` and the per-member `userLookup` miss falling back to `"Unknown"`)

**OrganizationMapper**
- [x] `ToDto` — all fields map correctly; `projectCount` parameter flows through; `IsActive` and `Description` included (done — `tests/Ordinis.UnitTests/Application/Organizations/Dtos/OrganizationMapperTests.cs`; also covers a `null` `Description` and `IsActive` reflecting a suspended organization)

**UserMapper**
- [x] `ToDto` — all fields map correctly; `OrganizationName` parameter flows through; auth-sensitive fields (`RefreshToken`, `RefreshTokenExpiresAt`, `PasswordHash`) are absent from the DTO (done — `tests/Ordinis.UnitTests/Application/Users/Dtos/UserMapperTests.cs`; the auth-sensitive-fields check uses reflection over `UserDto`'s properties so it fails if any of those three are ever re-introduced, not just when mapped; also covers `OrgRole` enum-to-string mapping and `IsActive` for a deactivated user)

**Git tag (after Part 3):** `v0.9-part3-mappers`

---

#### Part 4 — Task handler tests
> Follows the `AddAttachmentHandler` / `RemoveAttachmentHandler` pattern already established. Reuses `TestDbContextFactory` and `DomainFactory` from Part 1.

**Command handlers**
- [x] `AddAttachmentHandler` — attachment stored via `IFileStorageService`; `AttachmentAdded` domain event raised; attachment ID returned (done)
- [x] `RemoveAttachmentHandler` — attachment removed from task; `AttachmentRemoved` domain event raised; `IFileStorageService.DeleteAsync` called after DB save (done)
- [x] `CreateTaskHandler` — task created with correct fields; `TaskCreated` domain event raised; new task ID returned (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/CreateTaskHandlerTests.cs`; also covers immediate assignment when `AssigneeId` is supplied — `TaskAssigned` raised after `TaskCreated` — and a null `DueDate` baseline; no bugs found)
- [x] `UpdateTaskHandler` — `Title`, `Description`, `Priority`, `DueDate` updated; `DbUpdateConcurrencyException` caught and translated to `ConcurrencyException` (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/UpdateTaskHandlerTests.cs`; the concurrency case is forced deterministically by setting the tracked `RowVersion` property's `OriginalValue` via the change tracker, rather than racing two contexts against the same in-memory database, since `TestAppDbContext` now configures `RowVersion` as a concurrency token (`IsRowVersion()`) generically for every aggregate, mirroring what the real `AppDbContext`'s entity configurations will do in Phase 5; fixed a bug found while testing — `RequestedByUserId` was validated as non-empty but never consumed by the handler: `UpdateDetails`/`ChangePriority`/`SetDueDate` raised no domain event at all, so scalar task edits were invisible to the audit log and webhooks despite both being in-scope REST features. Replaced the three granular methods (`UpdateDetails`/`ChangePriority`/`SetDueDate`) with a single `ProjectTask.Update(...)` aggregate method that applies all four scalar fields and raises one `TaskUpdated` domain event carrying `RequestedByUserId`; the granular methods were removed entirely rather than kept as private helpers or left public, since nothing in `src` called them individually and leaving them public would have reopened the same bypass-the-event footgun. `TaskUpdated` carries `Changes` (`IReadOnlyDictionary<string, (object? Before, object? After)>`), keyed by property name, with an entry only for fields whose value actually changed — mirrors `TaskMoved`'s `PreviousStatus`/`NewStatus` precedent for giving the audit log a real diff, but stays generic and omits no-op fields instead of always carrying all four. `UpdateTaskHandler` and `ProjectTaskTests` updated to the new shape)
- [x] `DeleteTaskHandler` — soft delete applied (`IsDeleted = true`, `DeletedAt` set); `TaskDeleted` domain event raised (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/DeleteTaskHandlerTests.cs`; fixed a design gap found during testing — the original handler called `task.SoftDelete(now)` on the base class directly, bypassing domain events and leaving `RequestedByUserId` unused; introduced `ProjectTask.Delete(deletedByUserId, now)` which wraps `SoftDelete` and raises `TaskDeleted`, matching the same pattern as `Update()`/`TaskUpdated`; also added `TaskDeleted.cs` domain event record and three `ProjectTaskTests` unit tests covering the happy path, idempotency, and terminal-state deletion; note: "task no longer returned by default EF queries" is a production query-filter behaviour not testable in unit tests since `TestAppDbContext` has no global soft-delete filter — covered in integration tests)
- [x] `MoveTaskHandler` — status transition applied; `TaskMoved` domain event raised with correct previous and new status (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/MoveTaskHandlerTests.cs`; covers happy path with full `TaskMoved` payload assertion including `PreviousStatus`, invalid transition throwing `DomainException`, `NotFoundException` for unknown ID, and `DbUpdateConcurrencyException` translated to `ConcurrencyException` via the same stale-`OriginalValue` forcing technique used in `UpdateTaskHandlerTests`; fixed `public static readonly Now` → `private static readonly` and target type `new(...)` hint)
- [x] `AssignTaskHandler` — `AssigneeId` set; `TaskAssigned` domain event raised (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/AssignTaskHandlerTests.cs`; covers happy path with full `TaskAssigned` payload assertion, `NotFoundException` for unknown ID, `ArgumentException` for `Guid.Empty` assignee (domain guard, bypasses validator), `DomainException` for duplicate assignment to same user (`task.already-assigned`), and `ConcurrencyException` via stale `OriginalValue`; added missing class-level XML doc comment)
- [x] `UnassignTaskHandler` — `AssigneeId` cleared; `TaskUnassigned` domain event raised (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/UnassignTaskHandlerTests.cs`; covers happy path with full `TaskUnassigned` payload assertion including `PreviousAssigneeId`, `NotFoundException` for unknown ID, `DomainException` when task has no assignee (`task.already-unassigned`), and `ConcurrencyException` via stale `OriginalValue`; fixed stale `UnassignTask` command doc comment that incorrectly described unassign-when-already-unassigned as a no-op — the domain throws `DomainException`; added missing `Unassign_AlreadyUnassignedTask_ThrowsDomainException` test to `ProjectTaskTests`; added missing class-level XML doc comment)
- [x] `AddCommentHandler` — comment added to task; `CommentAdded` domain event raised; new comment ID returned (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/AddCommentHandlerTests.cs`; covers happy path with full `CommentAdded` payload assertion and returned comment ID verified against persisted comment, `NotFoundException` for unknown ID, `ArgumentException` for empty content (domain guard), and `DomainException` when task is soft-deleted; no concurrency test needed — handler has no `DbUpdateConcurrencyException` catch; fixed typo `autorId` → `authorId`, wrong event name in test method (`TaskCommentedEvent` → `CommentAddedEvent`), and missing class-level XML doc comment)
- [x] `EditCommentHandler` — `Content` updated; `IsEdited` set to `true` (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/EditCommentHandlerTests.cs`; covers happy path asserting Content and IsEdited, `NotFoundException` for unknown task, `NotFoundException` for unknown comment, `ArgumentException` for empty content (domain guard), and `DomainException` when comment is soft-deleted (`comment.update-deleted`); no concurrency test — comment edits are intentionally last-write-wins since only the author may edit (enforced by validator), making same-user double-submit the only conflict scenario; fixed `Guid.NewGuid()` → `Guid.CreateVersion7()`, wrong test name `HandleAsync_DeletedTask` → `HandleAsync_DeletedComment`, unused `requestedBy` variable, and missing class-level XML doc comment; added concurrency rationale to handler doc)
- [x] `RemoveCommentHandler` — comment soft deleted; `CommentRemoved` domain event raised (done — `tests/Ordinis.UnitTests/Application/Tasks/Commands/RemoveCommentHandlerTests.cs`; covers happy path with full `CommentRemoved` payload assertion, `NotFoundException` for unknown task, and `DomainException` for unknown comment ID (`task.comment-not-found`); no concurrency test — handler has no `DbUpdateConcurrencyException` catch, same rationale as `EditComment`; fixed assertion ordering (guard `Assert.Single` before accessing `.First()`), stale handler doc comment that incorrectly claimed `NotFoundException` guards missing comments (domain throws `DomainException`), and missing class-level XML doc comment)

**Query handlers**

- [x] `GetTaskByIdHandler` — returns correct `TaskDto` with embedded comments and attachments; throws `NotFoundException` when task does not exist (done — `tests/Ordinis.UnitTests/Application/Tasks/Queries/GetTaskByIdHandlerTests.cs`; covers all scalar fields, assignee display name resolved via cross-aggregate user lookup, comment embedding with `AuthorDisplayName` and `IsEdited`/`UpdatedAt` fields, soft-deleted comment excluded by mapper's `!c.IsDeleted` filter, attachment embedding with all DTO fields, and `NotFoundException` for unknown ID; `ConcurrencyToken` (Base64 RowVersion) not asserted in unit tests — InMemory EF Core does not generate row-version bytes; covered by integration tests; note: soft-deleted task also surfaces as `NotFoundException` in production via the global EF Core query filter — not testable in unit tests since `TestAppDbContext` has no filter)
- [x] `GetTasksFilteredHandler` — returns correct page of `TaskSummaryDto`; each filter param applied correctly in isolation; `TotalCount` matches pre-pagination count; sort ascending and descending; `PageSize` cap enforced (done — `tests/Ordinis.UnitTests/Application/Tasks/Queries/GetTasksFilteredHandlerTests.cs`; 12 tests covering no-filter baseline, all 6 filter params individually, second-page pagination with correct `TotalCount`/`Page`/`PageSize` metadata, sort by title ascending and descending, `PageSize` capped at 100 when 200 requested, and assignee display name resolved via batch user lookup in summary DTO)

**Git tag (after Part 4):** `v0.9-part4-task-handlers`

---

#### Part 5 — Project and Board handler tests
> Same pattern as Part 4.

**Project command handlers**
- [x] `CreateProjectHandler` — project created; slug auto-generated from name via `ISlugGenerator`; new project ID returned (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/CreateProjectHandlerTests.cs`; two bugs found and fixed: (1) `Project.Create` stored `""` instead of `null` for empty description — fixed to `string.IsNullOrWhiteSpace(description) ? null : description.Trim()`; (2) a test incorrectly expected the handler to append a `-1` suffix on slug collision — removed because collision prevention is the validator's responsibility, already covered by `CreateProjectValidatorTests`; added missing test verifying the creator is auto-added as an `Admin` `ProjectMember`)
- [x] `UpdateProjectHandler` — `Name` and `Description` updated; `DbUpdateConcurrencyException` translated to `ConcurrencyException` (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/UpdateProjectHandlerTests.cs`; bugs found and fixed: (1) `Project.UpdateDescription` had the same empty-string-to-null gap as `Project.Create` — fixed to `string.IsNullOrWhiteSpace(newDescription) ? null : newDescription.Trim()`; (2) all `ProjectBuilder.Create` calls used `DateTimeOffset.UtcNow` unnecessarily — removed, builder's `DefaultNow` is used; (3) typo in test name `EmpyName` → `EmptyName`; (4) unused `using Ordinis.Domain.Tasks` removed; added missing tests for `null` and empty `NewDescription` both clearing description to null)
- [x] `DeleteProjectHandler` — soft delete applied (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/DeleteProjectHandlerTests.cs`; issues found and fixed: (1) file/class name was `DeleteProjectHandlerTest` missing the plural `s` — renamed; (2) block-scoped namespace converted to file-scoped; (3) `Guid.NewGuid()` replaced with `Guid.CreateVersion7()` in the not-found test; no missing tests — `SoftDelete` is idempotent, so no double-delete case needed)
- [x] `ArchiveProjectHandler` — project archived; `IsArchived = true` (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/ArchiveProjectHandlerTests.cs`; no bugs found; covers valid archive, not-found → `NotFoundException`, already-archived → `DomainException`)
- [x] `UnarchiveProjectHandler` — project unarchived; `IsArchived = false` (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/UnarchiveProjectHandlerTests.cs`; no bugs found; covers valid unarchive, not-found → `NotFoundException`, not-archived → `DomainException`)
- [x] `AddProjectMemberHandler` — member added to project with correct role and `JoinedAt` (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/AddProjectMemberHandlerTests.cs`; no bugs found; covers valid add with role+`JoinedAt` assertions, not-found → `NotFoundException`, already-member → `DomainException` (triggered via creator auto-added as Admin), archived project → `DomainException`)
- [x] `RemoveProjectMemberHandler` — member removed from project (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/RemoveProjectMemberHandlerTests.cs`; no bugs found; covers valid remove verified via `ProjectMembers` DbSet, not-found → `NotFoundException`, not-a-member → `DomainException`, last-admin guard → `DomainException`, archived project → `DomainException`)
- [x] `ChangeMemberRoleHandler` — member role updated (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/ChangeMemberRoleHandlerTests.cs`; no bugs found; covers valid role change verified via `ProjectMembers` DbSet, not-found → `NotFoundException`, not-a-member → `DomainException`, demoting last admin → `DomainException`, archived project → `DomainException`)

**Board command handlers**
- [x] `CreateBoardHandler` — board created directly as independent aggregate root; new board ID returned (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/CreateBoardHandlerTests.cs`; no bugs found; covers valid create with field assertions including whitespace trimming, empty name → `ArgumentException`, empty `ProjectId` → `ArgumentException`)
- [x] `ArchiveBoardHandler` — board archived; `IsArchived = true` (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/ArchiveBoardHandlerTests.cs`; no bugs found; covers valid archive, not-found → `NotFoundException`, already-archived → `DomainException`)
- [x] `UnarchiveBoardHandler` — board unarchived; `IsArchived = false` (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/UnarchiveBoardHandlerTests.cs`; added during the `BoardsController` review alongside the handler itself, mirroring `UnarchiveProjectHandlerTests`; covers valid unarchive, not-found → `NotFoundException`, not-archived → `DomainException`)
- [x] `RenameBoardHandler` — board name updated (done — `tests/Ordinis.UnitTests/Application/Projects/Commands/RenameBoardHandlerTests.cs`; covers valid rename with whitespace-trim assertion, not-found → `NotFoundException`, empty name → `ArgumentException`, archived board → `DomainException`. **Bug found during the `BoardsController` review:** the handler never caught `DbUpdateConcurrencyException` at all, so a genuine race produced an unhandled `500` instead of `409` — fixed to match `UpdateProjectHandler`'s pattern; added `HandleAsync_RowVersionChangedSinceLoad_ThrowsConcurrencyException`, same stale-`OriginalValue` technique as `UpdateProjectHandlerTests`)

**Project query handlers**
- [x] `GetProjectByIdHandler` — returns correct `ProjectDto` with embedded boards and members; per-board task counts resolved correctly via grouped query; throws `NotFoundException` when not found (done — `tests/Ordinis.UnitTests/Application/Projects/Queries/GetProjectByIdHandlerTests.cs`; no bugs found; covers full happy path with field/count/display-name assertions, not-found → `NotFoundException`, per-board task count isolation via GroupBy, missing user row → `"Unknown"` display name fallback)
- [x] `GetProjectsFilteredHandler` — `OrganizationId` filter; `MemberId` filter; `IncludeArchived` flag; pagination and sort (done — `tests/Ordinis.UnitTests/Application/Projects/Queries/GetProjectsFilteredHandlerTests.cs`; no bugs found; covers excludes-archived-by-default, includeArchived flag, org filter, member filter, pagination, board/member count projection)
- [x] `GetProjectTasksHandler` — returns paged tasks scoped to all boards in the project (done — `tests/Ordinis.UnitTests/Application/Projects/Queries/GetProjectTasksHandlerTests.cs`; no bugs found; covers project-scoped task isolation, not-found, status filter using `Cancelled` via `Move()`, pagination)
- [x] `GetProjectMembersHandler` — returns all members for the project (done — `tests/Ordinis.UnitTests/Application/Projects/Queries/GetProjectMembersHandlerTests.cs`; no bugs found; covers ordered-by-JoinedAt with display names, not-found, missing user → `"Unknown"` fallback)

**Board query handlers**

- [x] `GetBoardByIdHandler` — returns correct `BoardDto` with embedded tasks (capped); throws `NotFoundException` when not found (done — `tests/Ordinis.UnitTests/Application/Projects/Queries/GetBoardByIdHandlerTests.cs`; no bugs found; covers correct BoardDto with TaskCount=2 and Tasks.Count=2, not-found, zero-task board with TaskCount=0 and empty Tasks)
- [x] `GetBoardTasksHandler` — returns paged tasks scoped to the board; filter and sort applied (done — `tests/Ordinis.UnitTests/Application/Projects/Queries/GetBoardTasksHandlerTests.cs`; no bugs found; covers board-scoped task isolation, not-found, status filter using `Cancelled` via `Move()`, pagination)

**Git tag (after Part 5):** `v0.9-part5-project-board-handlers`

---

#### Part 6 — Organization and User handler tests + Dispatcher tests

**Organization command handlers**

- [x] `CreateOrganizationHandler` — organization created; slug auto-generated and globally unique; new organization ID returned (done — `tests/Ordinis.UnitTests/Application/Organizations/Commands/CreateOrganizationHandlerTests.cs`; covers field assertions, slug generation, null description, and empty-name domain guard; no bugs found)
- [x] `RenameOrganizationHandler` — `Name` updated; `DbUpdateConcurrencyException` translated to `ConcurrencyException` (done — `tests/Ordinis.UnitTests/Application/Organizations/Commands/RenameOrganizationHandlerTests.cs`; covers name update, slug immutability, not-found → `NotFoundException`, suspended org → `DomainException`, stale RowVersion → `ConcurrencyException`; no bugs found)
- [x] `UpdateOrganizationDescriptionHandler` — `Description` updated; clears when `null`; concurrency exception translated (done — `tests/Ordinis.UnitTests/Application/Organizations/Commands/UpdateOrganizationDescriptionHandlerTests.cs`; covers update, null clears, not-found, suspended-org, concurrency; no bugs found)
- [x] `SuspendOrganizationHandler` — organization suspended; `IsActive = false` (done — `tests/Ordinis.UnitTests/Application/Organizations/Commands/SuspendOrganizationHandlerTests.cs`; covers happy path, not-found, already-suspended → `DomainException`; no bugs found)
- [x] `ReactivateOrganizationHandler` — organization reactivated; `IsActive = true` (done — `tests/Ordinis.UnitTests/Application/Organizations/Commands/ReactivateOrganizationHandlerTests.cs`; covers happy path, not-found, already-active → `DomainException`; no bugs found)

**Organization query handlers**

- [x] `GetOrganizationByIdHandler` — returns correct `OrganizationDto`; `projectCount` resolved via separate scalar query; throws `NotFoundException` when not found (done — `tests/Ordinis.UnitTests/Application/Organizations/Queries/GetOrganizationByIdHandlerTests.cs`; covers all fields, project count isolation across orgs, suspended org `IsActive = false`, not-found; note: `OrganizationDto` has no `Slug` field — the mapper doesn't expose the slug)
- [x] `GetOrganizationProjectsHandler` — returns paged `ProjectSummaryDto`; `IncludeArchived` flag; `MemberId` filter; throws `NotFoundException` when organization not found (done — `tests/Ordinis.UnitTests/Application/Organizations/Queries/GetOrganizationProjectsHandlerTests.cs`; covers org-scoped project isolation, not-found, excludes-archived-by-default, includeArchived flag, member filter, and pagination; no bugs found)

**User command handlers**

- [x] `CreateUserHandler` — plaintext password hashed via `IPasswordHasher` before `User.Create()`; domain never receives plaintext; new user ID returned (done — `tests/Ordinis.UnitTests/Application/Users/Commands/CreateUserHandlerTests.cs`; uses a `FakePasswordHasher` double; asserts `PasswordHash != plaintext` and `PasswordHash == fakeHasher.Hash(plaintext)`; also covers domain guards for empty `DisplayName` and `Email`; no bugs found)
- [x] `UpdateUserHandler` — `DisplayName` updated (done — `tests/Ordinis.UnitTests/Application/Users/Commands/UpdateUserHandlerTests.cs`; covers display name update, not-found, empty name → `ArgumentException`, stale RowVersion → `ConcurrencyException`; no bugs found)
- [x] `DeactivateUserHandler` — user deactivated; `IsActive = false` (done — `tests/Ordinis.UnitTests/Application/Users/Commands/DeactivateUserHandlerTests.cs`; covers happy path, not-found, already-inactive → `DomainException`; no bugs found)
- [x] `ReactivateUserHandler` — user reactivated; `IsActive = true` (done — `tests/Ordinis.UnitTests/Application/Users/Commands/ReactivateUserHandlerTests.cs`; covers happy path, not-found, already-active → `DomainException`; no bugs found)
- [x] `ChangeUserOrgRoleHandler` — org role updated (done — `tests/Ordinis.UnitTests/Application/Users/Commands/ChangeUserOrgRoleHandlerTests.cs`; covers role change, not-found, stale RowVersion → `ConcurrencyException`; no bugs found)

**User query handlers**

- [x] `GetUserByIdHandler` — returns correct `UserDto`; auth-sensitive fields absent; throws `NotFoundException` when not found (done — `tests/Ordinis.UnitTests/Application/Users/Queries/GetUserByIdHandlerTests.cs`; covers all DTO fields, `OrgRole` serialized as string, org name resolved via scalar lookup, missing org → empty string fallback, deactivated user `IsActive = false`, auth-sensitive field absence enforced by reflection; no bugs found)
- [x] `GetUserTasksHandler` — returns paged tasks assigned to the user; filter and sort applied (done — `tests/Ordinis.UnitTests/Application/Users/Queries/GetUserTasksHandlerTests.cs`; covers user-scoped task isolation, not-found, status filter (`Backlog → ToDo → InProgress` transition required — state machine does not allow direct Backlog→InProgress), pagination, assignee display name resolution; no bugs found)

**Dispatcher**

- [x] Valid command with passing validator reaches handler and returns result (done — `tests/Ordinis.UnitTests/Application/Common/DispatcherTests.cs`)
- [x] Invalid command fires `IValidator<T>` before handler; handler is never invoked; `ValidationException` thrown with correct field-level errors (done — same file; `handler.Invoked` flag asserts handler was not called)
- [x] Valid command with no registered validator reaches handler directly (done — same file)
- [x] Query bypasses the validation pipeline entirely regardless of whether a validator is registered (done — same file; fixed latent bug: `Dispatcher.QueryAsync` was calling `ValidateAsync` despite the design decision that queries bypass validation — removed the call; test registers an always-failing query validator and asserts no `ValidationException` is thrown)
- [x] Command with no registered handler throws `InvalidOperationException` (done — same file)

**Git tag (after Part 6):** `v0.9-part6-org-user-handlers-dispatcher`

### Integration tests (Ordinis.IntegrationTests)
- [x] `WebApplicationFactory<Program>` setup with test `appsettings.json` pointing to SQLite or a real test DB (done — chose a real test DB over SQLite: `tests/Ordinis.IntegrationTests/Infrastructure/OrdinisApiFactory.cs` runs the actual `Ordinis.Api` host in-process against a disposable SQL Server container (`Testcontainers.MsSql`), migrated via the real `Ordinis.Infrastructure.Migrations.SqlServer` migrations — SQLite was rejected because it can't faithfully exercise the `RowVersion` concurrency-token/provider-specific SQL behavior integration tests exist to catch. Required adding `public partial class Program;` to `src/Ordinis.Api/Program.cs` since top-level statements don't otherwise expose an accessible entry-point type for `WebApplicationFactory<Program>`. **Gotcha found and fixed:** `AddInfrastructureServices` reads `DatabaseProvider`/`ConnectionStrings:DefaultConnection` synchronously in `Program.cs`, before `builder.Build()` runs — `WebApplicationFactory`'s `ConfigureWebHost`/`ConfigureAppConfiguration` hooks only attach once `Build()` is invoked, so they're too late to reach that eager read. Fixed by setting `DatabaseProvider`/`ConnectionStrings__DefaultConnection`/`ASPNETCORE_ENVIRONMENT` as process environment variables before the host is first touched (`WebApplicationBuilder.CreateBuilder(args)` reads env vars at call time, so this reaches Program.cs in time). Also overrides the global rate limiter to a no-op via `ConfigureTestServices` — the production 100 req/min-per-IP limiter otherwise throttles rapid-fire `TestServer` requests, which all share one loopback partition key. Verified end-to-end with `tests/Ordinis.IntegrationTests/SmokeTests.cs`)
- [x] Shared `DatabaseFixture` — creates schema, seeds baseline data, resets between tests (done — implemented as `ApiCollection`/`IntegrationTestBase` rather than a single `DatabaseFixture` class: `ApiCollection` (`[CollectionDefinition]`) shares one `OrdinisApiFactory`/SQL Server container across every test class via `ICollectionFixture<T>`, since container startup + migrations take seconds and would dominate the run if repeated per test class; `IntegrationTestBase` resets table data after every test via Respawn (`OrdinisApiFactory.ResetDatabaseAsync`) so tests stay independent regardless of order. No baseline-data seeding yet — deferred until the first real controller tests define what baseline data they actually need; `IntegrationTestBase.CreateScope()` exposes a DI scope for tests to seed via `AppDbContext` directly)
**Packages added to `Ordinis.IntegrationTests.csproj`:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.9 | Provides `WebApplicationFactory<T>` — boots the real `Ordinis.Api` host in-process and exposes an `HttpClient` wired directly to its `TestServer`, so requests never leave the process. |
| `Testcontainers.MsSql` | 4.13.0 | Starts/stops a disposable `mcr.microsoft.com/mssql/server:2022-latest` Docker container per test run — a real SQL Server instance rather than SQLite/InMemory, so `RowVersion` concurrency tokens and provider-specific SQL behave exactly as in production. |
| `Respawn` | 7.0.0 | Resets table data (not schema) between tests by deleting rows in FK-safe dependency order — far cheaper than re-running migrations or recreating the database per test. |

- [x] API-level tests per controller (happy path + common error cases) (done — 133 tests across
  `tests/Ordinis.IntegrationTests/{Tasks,Organizations,Projects,Users}/*ControllerTests.cs` and
  `SmokeTests.cs`, all passing against a real SQL Server Testcontainer via `OrdinisApiFactory`.
  `move`/`assign` are **not** covered for Tasks — confirmed during review that no REST endpoints
  exist for them yet (`TasksController.Update`'s own doc comment says status/assignment changes
  are "Phase 7" dedicated endpoints, still unbuilt); `Boards` also got full coverage
  (`BoardsControllerTests.cs`, 20 tests) even though not originally listed as its own sub-bullet
  here, since `BoardsController` is a distinct controller from `ProjectsController`.
  - Tasks (26 tests): create, get, list/filter, update, delete, add/edit/remove comment,
    add/remove attachment
  - Organizations (18 tests): create, get, list projects, update, suspend, reactivate
  - Projects (43 tests): create, get, list, update, delete, archive/unarchive, add/remove member,
    change member role, get members, get boards, get tasks
  - Boards (20 tests): create, get, list tasks, rename, archive/unarchive
  - Users (24 tests): create, get, list tasks, update, change org role, deactivate, reactivate
  - **Bugs found and fixed via this test-writing work — 5 total instances of the same class**:
    `OrganizationsController.Update` composed two independent commands
    (`RenameOrganization`+`UpdateOrganizationDescription`) with separate `SaveChangesAsync` calls,
    so a valid name + an over-length description committed the rename before the description
    update's `422` — non-atomic partial write. Fixed by consolidating into a single
    `UpdateOrganization` command (one load, one save; see Phase 4 Step 3 note). Separately,
    4 controller actions documented a `404` their validators made unreachable (an existence-check
    `MustAsync` on a related ID always fails first with `422`, so the handler's `NotFoundException`
    path never runs): `TasksController.Create` (`BoardId`), `TasksController.EditComment`
    (combined task/comment/author check), `BoardsController.Create` (`ProjectId`), and
    `ProjectsController.AddMember` (`ProjectId`) — all four corrected to document only `422`.
  - **Known gaps, deliberately deferred, not silently dropped**: no `409 Conflict` test exists for
    any `Update`-style action (`TasksController.Update`, `OrganizationsController.Update`,
    `ProjectsController.Update`, `BoardsController.RenameBoard`, `UsersController.UpdateUser`,
    `UsersController.ChangeOrgRole`) despite all of them documenting it and catching
    `DbUpdateConcurrencyException` — see the "Concurrency conflict tests" item below, which covers
    this generally rather than per-action. `ProjectsController.GetProjects`'s
    `organizationId`/`memberId`/`includeArchived`/`sortBy`/pagination filters are also untested
    beyond the trivial list-all/empty cases.
  - Shared test infrastructure grew alongside the controllers: `IntegrationTestBase.SeedAsync<T>()`
    (opens a scope, resolves `AppDbContext`, runs a seed callback, disposes) and
    `CreateOrganization()`/`SeedOrganizationWithUserAsync()` (build an unsaved `Organization` with
    a globally-unique slug, or seed a full Organization+User pair) eliminate the
    open-scope-resolve-AppDbContext boilerplate and the Organization+User seed block that would
    otherwise be repeated in every controller's test file — extracted once 3 files had
    near-identical copies (`TasksControllerTests`, `BoardsControllerTests`,
    `ProjectsControllerTests`), matching this project's established "extract on the third
    duplicate" convention.
- [x] Concurrency conflict tests — load same entity in two contexts, update both, assert second `PUT` returns `409 Conflict`
  - 6 tests added, one per `Update`-style action with a `409` contract: `TasksController.Update`,
    `OrganizationsController.Update`, `ProjectsController.Update`, `BoardsController.RenameBoard`,
    `UsersController.UpdateUser`, `UsersController.ChangeOrgRole`.
  - No ETag/If-Match is wired at the HTTP layer yet (still pending — Phase 7), so the only way to
    trigger a genuine `409` today is a real `RowVersion` collision, not a client-supplied stale
    version header.
  - An initial `Task.WhenAll`-based approach (racing two concurrent `PUT`s and hoping their DB
    round trips overlapped) worked for 5/6 endpoints but was flaky for
    `BoardsController.RenameBoard` specifically when run in a full batch (timing-dependent, not a
    product bug — see `docs/INTEGRATION_TESTS.md`). Replaced with a deterministic mechanism:
    `ConcurrencyRaceInterceptor`, a test-only `ISaveChangesInterceptor` attached to `AppDbContext`
    via `AddInterceptors(...)` on a second `AddDbContext<AppDbContext>` call in
    `OrdinisApiFactory` (merely registering it as a DI service was tried first and silently
    didn't attach — EF Core requires `AddInterceptors` explicitly), pauses the first request's
    `SaveChangesAsync` immediately before its UPDATE; the test then runs a second request to
    completion (it saves normally, bumping `RowVersion`), then releases the first, guaranteeing it
    observes a stale `RowVersion` and gets a real `DbUpdateConcurrencyException` → `409`. No
    product code changed — only test infrastructure. See `docs/INTEGRATION_TESTS.md` for the full
    mechanism, a sequence diagram, and why every wait in the driving helper is bounded
    (`WaitAsync(TimeSpan)`) rather than unbounded.
  - **Follow-up audit found this was a systemic gap, not just missing test coverage.** CLAUDE.md
    mandates as a hard rule that "`DbUpdateConcurrencyException` must be caught in command handlers
    and translated to `409 Conflict`" — a full pass over every mutating endpoint found **13** that
    load and save a `RowVersion`-tracked aggregate but had no catch block at all, meaning a real
    conflict would surface as an unhandled `500`, not the documented `409`:
    `TasksController.Delete`, `ProjectsController.Delete`/`Archive`/`Unarchive`/`AddMember`/
    `ChangeMemberRole`/`RemoveMember`, `BoardsController.ArchiveBoard`/`UnarchiveBoard`,
    `OrganizationsController.Suspend`/`Reactivate`, `UsersController.DeactivateUser`/`ReactivateUser`.
    All 13 handlers fixed with the same `catch (DbUpdateConcurrencyException) → throw new
    ConcurrencyException(...)` pattern already used by the original 6, their controller actions'
    XML docs and `[ProducesResponseType]` attributes updated to document `409`, and a
    `_ConcurrentModification_Returns409` integration test added for every one of them (13 more
    tests, same `AssertConcurrentRequestsConflictAsync` mechanism — 19 concurrency tests total now).
    Three commands with existing catch blocks were confirmed out of scope: `MoveTask`/`AssignTask`/
    `UnassignTask` are already concurrency-safe but unreachable — no controller route calls them yet
    (deferred to Phase 7 per `TasksController`'s own doc-comment) — and `RenameOrganization`/
    `UpdateOrganizationDescription` are dead code superseded by the consolidated `UpdateOrganization`.
  - **Deeper finding: `AddMember`/`ChangeMemberRole`/`RemoveMember` could never actually conflict**,
    even with a correct catch block, until a real bug in `AppDbContext` was fixed. `ProjectMember`
    lives in its own table with no `RowVersion` of its own (owned by `Project` per the aggregate
    boundary, but not EF-Core-owned/embedded) — so adding, updating, or removing one only touches
    the `ProjectMembers` table; `Project`'s own row (and its `RowVersion`) was never included in the
    `UPDATE` batch, since EF Core only reassigns a tracked `AggregateRoot`'s `RowVersion` when that
    entity itself is `Added`/`Modified` (`AppDbContext.SetConcurrencyTokens`). Confirmed empirically:
    the new `AddMember_ConcurrentModification_Returns409` test initially failed with both requests
    returning success, not one 409 — a structural gap, not a flaky test. Fixed at the infrastructure
    level (not per-handler) with `AppDbContext.MarkAggregateRootsDirtyForChangedChildren()`, called
    from `SetConcurrencyTokens()`: it walks every changed non-root tracked entry, resolves its
    owning aggregate root via foreign-key *values* (not navigation-fixup, which EF Core severs for
    `Deleted` entries — confirmed via a second failing test, `RemoveMember_ConcurrentModification`,
    that `ReferenceEntry.TargetEntry` is `null` once an entity is marked `Deleted`) matched against
    `INavigation.Inverse.IsCollection` to distinguish real ownership (`ProjectMember.Project`, whose
    inverse `Project.Members` is a collection) from unrelated references (`ProjectMember.User`, which
    has no such inverse — `User.ProjectMemberships` was already removed per the note above), and
    marks that owner `Modified` so it gets a fresh `RowVersion` and a genuine optimistic-concurrency
    check. This is a general, one-time fix in the shared save pipeline, not scattered per-handler
    logic — it will also protect any future aggregate/child-table pair without further changes.
- [x] Validation error tests — submit invalid payloads, assert `422 Unprocessable Entity` with correct error fields
  - A survey found FluentValidation failures already map to a real, well-defined contract — RFC
    9457 `ValidationProblemDetails` with a per-field `errors` dictionary (`Dispatcher.ValidateAsync`
    groups errors by `PropertyName`; `ProblemDetailsFactory.CreateValidation` builds the response)
    — but of the 34 existing `422` tests at the time, every single one checked status code only,
    never the body. Added `IntegrationTestBase.AssertValidationProblemAsync(response, expectedField,
    expectedMessageSubstring?)`, which asserts status `422`, the `Title`, and that `errors` contains
    the expected field key. Retrofitted 5 existing tests (one `Create_EmptyName`-style test per
    controller) as canonical examples; left the other 29 as status-code-only by deliberate scope
    decision.
  - Cross-referenced all 22 `AbstractValidator<T>` classes' rules against integration coverage.
    21 already have thorough per-field FluentValidation unit tests (`TestValidate`/
    `ShouldHaveValidationErrorFor`), so new integration tests target the HTTP contract and
    endpoint wiring, not re-deriving exact per-field messages. Added ~30 new tests filling every
    genuinely-reachable gap: duplicate-slug uniqueness, over-length descriptions, invalid enum
    values (`Role`/`Priority` via an out-of-range cast — enums serialize numerically by default,
    so this round-trips through JSON and only `IsInEnum()` rejects it), nonexistent
    assignee/reporter/uploader IDs, over-length titles/content/names, and the `AddAttachment`
    validator (`FileName`, `ContentType`, `SizeInBytes`, `UploadedByUserId`), which had
    essentially zero coverage before this pass.
  - Added the one missing validator unit test file, `UpdateProjectValidatorTests.cs` — the only
    validator in the codebase without dedicated unit coverage.
  - `MoveTask`/`AssignTask` and `RenameOrganization`/`UpdateOrganizationDescription` showed as
    "zero coverage" in the audit but are **not gaps**: no controller route calls them (Phase 7
    dependency / dead code respectively, both already noted elsewhere in this file) — nothing to
    test.
  - Two validator rules turned out to be structurally unreachable via real HTTP requests, the same
    class of finding as other unreachable-rule cases already documented in this file:
    `AddAttachmentValidator.FileStream.NotNull()` (`[FromForm] IFormFile file` is a required action
    parameter; omitting the file part fails ASP.NET Core model binding before the validator runs)
    and `FileName.NotEmpty()` (confirmed empirically — even manually constructing a
    `Content-Disposition` header with an empty filename gets ASP.NET Core's own `IFormFile` binder
    to reject it with `400 Bad Request` before the request reaches the validator). No test is
    possible for either without bypassing normal HTTP semantics.
  - `dotnet build`: 0 warnings, 0 errors. `Ordinis.UnitTests`: 584/584 passing (575 + 9 new).
    `Ordinis.IntegrationTests`: 178/178 passing (152 + 26 new).
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
  *(started early: the scaffold-generated `src/Ordinis.Api/Ordinis.Api.http` is being updated
  incrementally as each controller is built in Phase 6, rather than written from scratch here —
  reuse/finish that file in Phase 10 instead of creating a new `requests.http`)*
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

- [x] `Dockerfile` — multi-stage build (sdk → publish → runtime); non-root user; `EXPOSE 8080`
- [x] `docker-compose.yml` — services: `api` + `db` (SQL Server or PostgreSQL selectable); volume for DB data; health check on `api`
- [x] `docker-compose.override.yml` — local dev overrides (e.g. mount source for hot reload)
- [x] GitHub Actions — `ci.yml`:
  - Trigger: `push` to any branch, `pull_request` to `main`
  - Steps: checkout → setup .NET 10 → restore → build → test → lint (via `dotnet format --verify-no-changes`)
  - Test results uploaded as artifact
- [x] GitHub Actions — `publish.yml`:
  - Trigger: `push` to `main` (after squash merge)
  - Steps: build Docker image → push to GitHub Container Registry (`ghcr.io`)
  - Tagged with git SHA and `latest`
- [x] Environment-specific `appsettings`:
  - `appsettings.json` — defaults, no secrets
  - `appsettings.Development.json` — verbose logging, CORS allow-all
  - `appsettings.Production.json` — minimal logging, strict CORS
- [x] GitHub Actions Secrets for CI: `CONNECTION_STRING`, `JWT_SIGNING_KEY`; injected as environment variables into the test and publish steps
- [x] Document secrets strategy in README: User Secrets (local) → GitHub Actions Secrets (CI) → environment variables (Docker/production)

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
