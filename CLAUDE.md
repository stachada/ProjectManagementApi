# Project Management API — Claude Context

> Jira-like REST API built with ASP.NET Core and Clean Architecture.
> This is a portfolio project targeting senior .NET developers, demonstrating
> advanced REST API design patterns — not just CRUD.

For the full phase-by-phase build plan, task checklists, and design decision log,
see **[BUILD_PLAN.md](./BUILD_PLAN.md)**.

---

## Domain

A Jira/Trello-like project management API.

**Core entities:** `Organization`, `Project`, `Board`, `Task`, `Comment`, `Attachment`, `User`

**Key workflows:**
- Task state transitions: move, assign, close, reopen
- Board management across projects
- Comments and attachments on tasks
- Audit logs and webhooks on task events

---

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# 13 |
| Framework | ASP.NET Core (Controllers + Minimal APIs — see API style below) |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / Api) |
| CQRS | Manual dispatch — `ICommandHandler<T>` / `IQueryHandler<T, TResult>`, no MediatR |
| ORM | EF Core injected directly into handlers — no repository pattern, no unit of work |
| Database | SQL Server + PostgreSQL; provider selected via `appsettings.json` (`DatabaseProvider`) |
| Read queries | Dapper for complex reporting / read queries |
| Validation | FluentValidation, wired manually into command handlers |
| Mapping | Manual (static mapper classes or extension methods); Mapster only if boilerplate becomes excessive |
| Logging | Serilog with structured logging, correlation IDs, request/response middleware |
| Auth | JWT + refresh tokens; role-based (Admin, Member, Viewer) + policy-based authorization |
| Docs | .NET 10 built-in OpenAPI + Scalar UI |
| Testing | xUnit; `WebApplicationFactory` for integration tests |
| CI/CD | Docker + docker-compose; GitHub Actions |

---

## Hard constraints — never suggest these

- ❌ No MediatR
- ❌ No AutoMapper
- ❌ No Swashbuckle / NSwag
- ❌ No repository pattern or unit of work abstraction over EF Core
- ❌ No `DateTimeOffset.UtcNow` (or `DateTime.Now`/`DateTime.UtcNow`) calls anywhere in Domain or Infrastructure — Application-layer command handlers inject `TimeProvider`, resolve `now` once per command, and pass it into domain methods as an explicit `DateTimeOffset` parameter

---

## Project structure

Feature-folder (vertical slice) layout within each layer. Organized by domain concept first,
then by type within that concept.

```
src/
├── Ordinis.Domain
│   ├── Tasks/              # ProjectTask.cs, TaskStatus.cs, TaskCreated.cs, Comment.cs, Attachment.cs
│   ├── Projects/           # Project.cs, Board.cs, ProjectMember.cs
│   ├── Organizations/      # Organization.cs
│   └── Users/              # User.cs, Role.cs
│
├── Ordinis.Application
│   ├── Tasks/
│   │   ├── Commands/       # CreateTask.cs, MoveTask.cs, AssignTask.cs ...
│   │   ├── Queries/        # GetTaskById.cs, GetTasksFiltered.cs ...
│   │   ├── Validators/     # CreateTaskValidator.cs ...
│   │   └── Dtos/           # TaskDto.cs
│   ├── Projects/
│   ├── Organizations/
│   ├── Users/
│   └── Common/             # ICommandHandler.cs, IQueryHandler.cs, Dispatcher.cs
│
├── Ordinis.Infrastructure
│   ├── Tasks/              # ProjectTaskConfiguration.cs (IEntityTypeConfiguration<ProjectTask>)
│   ├── Projects/
│   ├── Organizations/
│   ├── Users/
│   └── Persistence/        # AppDbContext.cs, migrations, DI registration
│
├── Ordinis.Api
│   ├── Tasks/              # TasksController.cs
│   ├── Projects/           # ProjectsController.cs
│   ├── Organizations/
│   ├── Users/
│   ├── MinimalApis/        # Auth.cs, Webhooks.cs (Minimal API endpoint groups)
│   └── Common/             # Middleware, filters, startup extensions
│
tests/
├── Ordinis.UnitTests
└── Ordinis.IntegrationTests
```

Shared infrastructure (`AppDbContext`, middleware, DI registration) lives in
`Common/` or `Persistence/` — not tied to a feature folder.

---

## API style

- **Controllers** for all resource endpoints (`Tasks`, `Projects`, `Boards`, `Comments`, `Users`, `Organizations`)
- **Minimal APIs** for focused, non-resource routes: auth (`/auth/login`, `/auth/refresh`), webhooks, search

Do not suggest Minimal APIs for resource endpoints. Do not suggest Controllers for auth or webhooks.

---

## Key design decisions

| Topic | Choice | Reason |
|---|---|---|
| CQRS dispatch | Manual (`ICommandHandler` / `IQueryHandler` + DI dispatcher) | Explicit, low-indirection; no hidden pipeline magic |
| Mapping | Manual static mappers / extension methods | Zero overhead, compiler-safe; no reflection-based magic |
| Validation | FluentValidation in command handlers | Rich rules; wired explicitly, not via pipeline behavior |
| Primary key type | `Guid.CreateVersion7()` (UUIDv7) | Sequential, time-ordered Guid — avoids clustered index fragmentation from random v4 Guids while keeping client-side ID generation before `SaveChanges` |
| Time abstraction | `TimeProvider` injected into `AppDbContext` and into Application-layer command handlers — Domain never references `TimeProvider` or `DateTimeOffset.UtcNow` | `AppDbContext` (constructor DI) sets `CreatedAt`/`UpdatedAt` automatically. For everything domain-meaningful (`IDomainEvent.OccurredAt`, `DeletedAt`, `JoinedAt`, `UploadedAt`), command handlers resolve `now` once via their own injected `TimeProvider` and pass it into aggregate methods as a plain `DateTimeOffset` parameter (e.g. `task.Move(status, userId, now)`, `comment.SoftDelete(now)`). Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`. Production registers `TimeProvider.System` as a singleton. |
| EF Core | Direct `AppDbContext` injection into handlers | No leaky abstraction; testable via integration tests |
| Soft deletes | `IsDeleted` / `DeletedAt` + global EF Core query filter | No data loss; filter applied transparently |
| Concurrency | `RowVersion` token + ETag / `If-Match` header | End-to-end optimistic concurrency; `409 Conflict` on collision |
| Domain events | Outbox pattern (DB + background job) | Reliable delivery without distributed transactions |
| Observability | Serilog + `X-Correlation-ID` + request logging middleware | Full per-request traceability across logs and responses |
| Secrets | User Secrets (dev) → GitHub Actions Secrets (CI) → env vars (prod) | Standard .NET approach; nothing in Git |
| Docs | .NET 10 built-in OpenAPI + Scalar UI | No Swashbuckle dependency; modern interactive UI |

---

## Code style expectations

- **Always produce complete, compilable C# code** — no partial snippets, no pseudocode, no `// TODO: implement`
- Separate `IEntityTypeConfiguration<T>` classes for all EF Core entity configuration — never inline in `OnModelCreating`
- Explicit and readable over clever: prefer clarity at the call site over concise one-liners
- XML doc comments on all public API surface: controllers, DTOs, public interfaces
- No Data Annotations on domain entities — use EF Core Fluent API exclusively
- `DbUpdateConcurrencyException` must be caught in command handlers and translated to `409 Conflict` with a Problem Details body

---

## Solution quality expectations

- Apply SOLID principles and Clean Architecture guidelines; call them out explicitly in suggestions
- Always recommend the optimal solution for the given context, not just the first viable one
- When multiple valid approaches exist, present them with concise pros/cons before recommending one
- Call out trade-offs explicitly (simplicity vs. flexibility, performance vs. maintainability, etc.)
- If the approach in BUILD_PLAN.md is suboptimal for a specific task, flag it and explain why — then proceed with the agreed approach unless asked to deviate

---

## REST features in scope

- Full CRUD on all resources
- Resource relationship endpoints (`GET /projects/{id}/tasks`, `GET /tasks/{id}/comments`)
- Filtering, sorting, pagination, sparse fields, search
- State transition endpoints (`POST /tasks/{id}/move`, `/assign`, `/close`, `/reopen`)
- HATEOAS (`_links` on task and project responses)
- Optimistic concurrency via ETags and `If-Match`
- Idempotency keys on POST endpoints (`Idempotency-Key` header)
- API versioning (`/api/v1`, `/api/v2`)
- Problem Details error responses (RFC 9457) across all endpoints
- Rate limiting (ASP.NET Core built-in middleware)
- Response caching headers
- Webhooks (fire on task events)
- Audit log endpoints

---

## How to work on tasks

Prompt pattern:

> **"Let's work on Phase X — [task description]"**

Examples:
- *"Let's work on Phase 2 — define the Task aggregate root"*
- *"Let's work on Phase 4 — configure dual-provider DbContext"*
- *"Let's work on Phase 6 — implement ETags and If-Match"*
- *"Let's work on Phase 7 — JWT + refresh token flow"*

You can also paste existing code and ask to review, extend, or debug it
in the context of a specific phase or task.

Before starting any task, check `BUILD_PLAN.md` for phase dependencies
to confirm prerequisites are satisfied.
