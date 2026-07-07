# CI/CD & Docker

How the GitHub Actions workflows run, what secrets they need, how the Docker image is built and
published, and gotchas hit while setting this up.

## Workflows

Both live in `.github/workflows/` and run automatically — nothing to trigger manually.

### `ci.yml` — every push, every PR to `main`

```
push (any branch) / pull_request (→ main)
  → restore → build → test → dotnet format --verify-no-changes
```

The lint step runs `dotnet format Ordinis.slnx --verify-no-changes` and fails the build on any
violation. Run `dotnet format Ordinis.slnx` locally before pushing to avoid a surprise failure.

The test step injects two repository secrets as environment variables:

```yaml
env:
  DatabaseProvider: SqlServer
  ConnectionStrings__DefaultConnection: ${{ secrets.CONNECTION_STRING }}
  Jwt__SigningKey: ${{ secrets.JWT_SIGNING_KEY }}
```

(ASP.NET Core's configuration binder reads `__` as the `:` section separator, so
`ConnectionStrings__DefaultConnection` → `ConnectionStrings:DefaultConnection`.)

> **Neither secret is exercised today.** `tests/Ordinis.IntegrationTests` currently has no test
> files, so `dotnet test` never touches a real database or the JWT signing key — CI passes
> without a live DB. The env vars are wired up ahead of time for when integration tests land
> (Phase 9 in `BUILD_PLAN.md`). See [Adding a real database to CI](#adding-a-real-database-to-ci)
> below for what that will need.

### `publish.yml` — push to `main`

```
push → main
  → log in to GHCR → docker build → docker push (tags: sha-<commit>, latest)
```

Uses the auto-provided `GITHUB_TOKEN` — no extra credentials needed for the GHCR push. Produces
two tags: the full commit SHA (permanent, traceable) and `latest` (always the newest `main`
build). Pull it with:

```bash
docker pull ghcr.io/<owner>/<repo>:latest
```

(GHCR package visibility follows the repository's visibility by default — flip it independently
under the package's own **Package settings** if needed.)

## Required repository secrets

Settings → Secrets and variables → Actions → New repository secret:

| Secret | Used by | Value |
|---|---|---|
| `CONNECTION_STRING` | `ci.yml` | A DB connection string (currently unused until Phase 9 adds integration tests — see below) |
| `JWT_SIGNING_KEY` | `ci.yml` | Same value you use locally — see [docs/LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md#jwt-signing-key) |

Secret values are masked in logs (`***`) and can't be read back once saved, only overwritten.

## Adding a real database to CI

Not implemented yet — this is the recipe for when `Ordinis.IntegrationTests` gets real tests.
GitHub Actions can run a database as a **service container** — a sidecar scoped to the job, torn
down automatically afterward, no external server needed. Add a `services:` block to the
`build-and-test` job in `ci.yml`, then point `CONNECTION_STRING` at `localhost` on the mapped port
(service containers are reachable at `localhost`, not by service name — that DNS name only exists
inside Docker Compose's own network, not on the runner host).

```yaml
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    services:                     # ← add here, before steps
      mssql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: Y
          SA_PASSWORD: ${{ secrets.SA_PASSWORD }}
          MSSQL_PID: Developer
        ports:
          - 1433:1433
        options: >-
          --health-cmd "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $SA_PASSWORD -C -Q 'SELECT 1'"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 10
          --health-start-period 30s
```

`CONNECTION_STRING` secret would then be:
`Server=localhost,1433;Database=Ordinis;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;`

Swap for PostgreSQL with `postgres:17`, `POSTGRES_DB`/`POSTGRES_USER`/`POSTGRES_PASSWORD`, port
`5432`, and health check `pg_isready -U ordinis -d Ordinis`; connection string becomes
`Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=<POSTGRES_PASSWORD>;`.
Requires an extra `SA_PASSWORD` / `POSTGRES_PASSWORD` repository secret alongside the two above.

A service container starts empty — schema still has to be applied before tests can hit it. See
the next section for the options.

### Applying migrations to that database

This project deliberately does **not** call `Database.Migrate()` at app startup (see
[docs/MIGRATIONS.md](MIGRATIONS.md#applying-migrations)), so a CI job needs an explicit step.
Three ways to do it, roughly in order of how much they resemble a real deploy:

| Option | How | Pros | Cons |
| --- | --- | --- | --- |
| **`dotnet ef database update`** | CI step runs the CLI directly against the service container | Simplest to wire up; one line per provider | Needs the `dotnet-ef` tool available in the job (see below); applies whatever the latest migration is with no review step |
| **Idempotent SQL script** | `dotnet ef migrations script --idempotent` generates SQL as a build artifact; a separate step/tool (`sqlcmd`/`psql`) executes it | Matches how you'd actually roll this out to staging/prod — the exact SQL is visible and diffable in CI logs before it runs | One extra step; needs the right CLI (`sqlcmd` or `psql`) on the runner |
| **Test-fixture-driven migrate** | The integration test project's `WebApplicationFactory` (or a `CollectionFixture`) calls `context.Database.Migrate()` once per test run, scoped to that process only | No CI wiring at all — self-contained in test code; works the same locally and in CI | Only acceptable inside test fixtures, never in `Ordinis.Api/Program.cs` — mixing it into the real app would reintroduce the auto-migrate-on-startup problem this project explicitly avoided |

**Recommended for this project once Phase 9 integration tests land:** the test-fixture approach
for the integration test project itself (simplest, and identical behavior locally vs. CI), plus
the idempotent-script approach as a separate, explicit step if a staging/production deploy
pipeline is added later — the two aren't mutually exclusive since they solve different problems
(exercising tests vs. rolling out a real deploy).

**`dotnet ef database update` as a CI step**, run once per provider job/matrix leg:

```yaml
- name: Apply migrations (SQL Server)
  run: >
    dotnet ef database update
    --project src/Ordinis.Infrastructure.Migrations.SqlServer
    --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
    --connection "${{ secrets.CONNECTION_STRING }}"
```

Swap the `--project`/`--startup-project` pair for
`src/Ordinis.Infrastructure.Migrations.PostgreSql` on the PostgreSQL leg — never point either at
`Ordinis.Api`, for the same reason called out in
[docs/MIGRATIONS.md](MIGRATIONS.md#adding-a-migration): it references both migration satellite
projects, so `dotnet ef` can resolve the wrong provider's design-time factory.

**Idempotent SQL script**, generated once and applied as its own step — useful if you want the
exact SQL visible in the CI log before it runs against anything:

```yaml
- name: Generate migration script (SQL Server)
  run: >
    dotnet ef migrations script --idempotent
    --project src/Ordinis.Infrastructure.Migrations.SqlServer
    --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
    --output migrate.sql

- name: Apply migration script
  run: >
    sqlcmd -S localhost,1433 -U sa -P "${{ secrets.SA_PASSWORD }}" -C
    -d Ordinis -i migrate.sql
```

(PostgreSQL equivalent: swap the generation step's project/output as above, then
`psql "$CONNECTION_STRING" -f migrate.sql` to apply.)

**`dotnet-ef` tool availability in CI.** GitHub's `ubuntu-latest` runner does not ship the
`dotnet-ef` CLI tool — it has to be installed or restored before either option above can run.
Two ways to get it:

```yaml
# Option 1: install globally, matching the version used locally
- run: dotnet tool install --global dotnet-ef --version 10.0.9

# Option 2 (preferred): commit a local tool manifest so the version is pinned
# and identical across every dev machine and CI — one-time setup:
#   dotnet new tool-manifest        # creates .config/dotnet-tools.json
#   dotnet tool install dotnet-ef --version 10.0.9
# then in CI:
- run: dotnet tool restore
```

This repo doesn't have a `.config/dotnet-tools.json` yet — it currently relies on `dotnet-ef`
being installed globally on whatever machine runs migration commands (see
[docs/MIGRATIONS.md](MIGRATIONS.md)). Worth adding a local tool manifest before wiring migrations
into CI, since a version drift between the tool and the runtime produces a real warning — this
was hit locally in this project: `The Entity Framework tools version '10.0.5' is older than that
of the runtime '10.0.9'`. A pinned manifest keeps every environment (dev machines and CI) on the
exact same tool version instead of whatever happens to be installed globally.

## Reading workflow results

**Actions tab** → pick **CI** or **Publish** → select a run → expand **build-and-test** (or
**publish**) → click any step to see its log. A red step means a non-zero exit code; later steps
still run if marked `if: always()` (the test-results upload step uses this so `.trx` files are
saved even on failure).

**Test results**: at the bottom of a run's summary page, under **Artifacts**, download
**test-results** — a zip of `.trx` files, viewable in Visual Studio (**Test → Import Test
Results**) or any TRX viewer.

**Branch protection**: once CI has run at least once on a branch, enable **Settings → Branches →
Branch protection rules** for `main` → **Require status checks to pass before merging** to block
merges on a red CI run.

## Docker build gotchas

- **`.dockerignore` must exclude `**/obj/` and `**/bin/`.** Without it, Windows-generated
  `project.assets.json` files (containing Windows-only paths like `C:\Program Files\...`) get
  copied into the Linux build context and break restore with a cryptic "Unable to find fallback
  package folder" error. This repo's `.dockerignore` already excludes both, plus `.git/`,
  `.github/`, `tests/`, and `*.md`.
- The Dockerfile only restores/publishes `Ordinis.Api` — test projects aren't needed in the image.
  Whenever a new project is added that `Ordinis.Api` depends on (like the migration satellite
  projects — see [docs/MIGRATIONS.md](MIGRATIONS.md)), its `.csproj` needs its own `COPY` line in
  the Dockerfile's restore stage, or the layer-cached restore silently won't see it.

## Line endings: CRLF vs LF between Windows dev and Linux CI

`dotnet format --verify-no-changes` in CI was failing with `ENDOFLINE: Fix end of line marker.
Replace 1 characters with '\r\n'.` errors. Root cause: `.editorconfig` demanded
`end_of_line = crlf`. Windows checks out files as CRLF, so `dotnet format` never complained
locally — the Linux CI runner checks out LF, so the same rule failed there. Format check passing
on Windows and failing on Linux for the same commit is the trap: the two environments disagree on
what "correct" looks like unless line endings are pinned explicitly.

Fixed with two files, both already in place in this repo:

1. `.editorconfig`: `end_of_line = lf`
2. `.gitattributes` at repo root: `* text=auto eol=lf` (normalizes all text files to LF on commit
   and checkout; binary extensions like `*.png`/`*.ico` are marked `binary` so they're untouched)

Day-to-day impact is none — on Windows, git's `core.autocrlf = true` still checks out CRLF on disk
so editors see no change; on commit, git normalizes to LF before storing, so CI always sees LF.

## Three-tier secrets strategy

| Environment | Mechanism |
|---|---|
| Local dev | [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) — see [docs/LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md) |
| CI (GitHub Actions) | Repository secrets — injected as env vars per workflow run |
| Docker / production | Environment variables via `.env` (compose) or `docker run -e` |

Nothing sensitive goes into `appsettings.json` or gets committed. All three tiers must carry the
**same** `Jwt:SigningKey` value once JWT auth (Phase 8) is implemented — a mismatch means tokens
signed in one environment fail verification in another.
