# Ordinis

A Jira/Trello-like project management REST API built with ASP.NET Core and Clean
Architecture. This is a portfolio project aimed at demonstrating advanced REST API
design patterns — not just CRUD — for a senior .NET audience.

## Overview

Ordinis models the core workflow of a project management tool: organizations contain
projects, projects contain boards, boards contain tasks, and tasks carry comments,
attachments, state transitions, and an audit trail. The API layer goes beyond basic
CRUD to cover filtering/sorting/pagination, HATEOAS, optimistic concurrency, API
versioning, idempotency, rate limiting, and webhooks.

For the full phase-by-phase build plan and design decision log, see
[BUILD_PLAN.md](./BUILD_PLAN.md).

## Architecture

Clean Architecture with four layers, each using a feature-folder (vertical slice)
structure organized by domain concept:

```
src/
├── Ordinis.Domain          # Entities, value objects, domain events — no external dependencies
├── Ordinis.Application     # CQRS commands/queries, handlers, validators, DTOs
├── Ordinis.Infrastructure  # EF Core, Dapper, persistence, logging, background jobs
└── Ordinis.Api             # Controllers (resources) + Minimal APIs (auth/webhooks/search)

tests/
├── Ordinis.UnitTests
└── Ordinis.IntegrationTests
```

**Core entities:** `Organization`, `Project`, `Board`, `ProjectTask`, `Comment`, `Attachment`, `User`, `ProjectMember`

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# 13 |
| Framework | ASP.NET Core (Controllers for resources, Minimal APIs for auth/webhooks/search) |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / Api) |
| CQRS | Manual dispatch — `ICommandHandler<T>` / `IQueryHandler<T, TResult>` (no MediatR) |
| ORM | EF Core injected directly into handlers (no repository pattern, no unit of work) |
| Database | SQL Server + PostgreSQL — provider selected via `appsettings.json` |
| Read queries | Dapper for complex reporting / read queries |
| Validation | FluentValidation, resolved per-type via `IValidator<T>` and run centrally in the dispatcher |
| Mapping | Manual (static mapper classes / extension methods) — no AutoMapper |
| Logging | Serilog with structured logging, correlation IDs, request/response middleware |
| Auth | JWT + refresh tokens, role-based (Admin, Member, Viewer) + policy-based authorization |
| Docs | .NET 10 built-in OpenAPI + Scalar UI (no Swashbuckle/NSwag) |
| Testing | xUnit, `WebApplicationFactory` for integration tests |
| CI/CD | Docker + docker-compose, GitHub Actions |

## REST features

- Full CRUD on all resources
- Resource relationship endpoints (`GET /projects/{id}/tasks`, `GET /tasks/{id}/comments`)
- Filtering, sorting, pagination, sparse fields, search
- State transition endpoints (`POST /tasks/{id}/move`, `/assign`, `/close`, `/reopen`)
- HATEOAS (`_links` on task and project responses)
- Optimistic concurrency via ETags and `If-Match`
- Idempotency keys on POST endpoints (`Idempotency-Key` header)
- API versioning (`/api/v1`, `/api/v2`)
- Problem Details error responses (RFC 9457)
- Rate limiting and response caching headers
- Webhooks fired on task events
- Audit log endpoints

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server or PostgreSQL (selectable via configuration)

### Configuration

The database provider is selected in `appsettings.json`:

```json
{
  "DatabaseProvider": "SqlServer" // or "PostgreSQL"
}
```

Connection strings and other secrets are kept out of source control:

- **Local development** — [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):
  ```bash
  cd src/Ordinis.Api
  dotnet user-secrets init
  dotnet user-secrets set "ConnectionStrings:Default" "<your-connection-string>"
  ```
- **CI** — GitHub Actions Secrets
- **Production** — environment variables

### Build and run

```bash
dotnet restore
dotnet build
dotnet run --project src/Ordinis.Api
```

### Run tests

```bash
dotnet test
```

## Project status

This project is under active development, built incrementally phase by phase.
See [BUILD_PLAN.md](./BUILD_PLAN.md) for the current checklist and architecture
decisions behind each phase.

| Phase | Status |
| --- | --- |
| 1 — Repository & solution setup | ✅ Complete |
| 2 — Domain layer | ✅ Complete |
| 3 — Application layer: infrastructure | ✅ Complete |
| 4 — Application layer: features (Tasks; Projects & Boards done — Organizations, Users, shared infra remain) | 🚧 In progress |
| 5 — Infrastructure layer | ⏳ Not started |
| 6 — API layer: core endpoints | ⏳ Not started |
| 7 — API layer: advanced REST features | ⏳ Not started |
| 8 — Security | ⏳ Not started |
| 9 — Testing & benchmarking | ⏳ Not started |
| 10 — Developer experience & docs | ⏳ Not started |
| 11 — CI/CD & Docker | ⏳ Not started |
| 12 — Polish & portfolio hardening | ⏳ Not started |

## License

Licensed under the [MIT License](./LICENSE).