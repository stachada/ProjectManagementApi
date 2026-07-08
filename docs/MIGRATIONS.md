# EF Core migrations — dual-provider workflow

This project supports both SQL Server and PostgreSQL for the same `AppDbContext`
(`src/Ordinis.Infrastructure/Persistence/AppDbContext.cs`). Migrations for the two providers
cannot live in the same assembly — EF Core throws *"More than one model snapshot was found for
context 'AppDbContext'"* if you try. Instead, each provider has its own satellite class library:

```
src/Ordinis.Infrastructure.Migrations.SqlServer/
├── DesignTime/AppDbContextFactory.cs   # IDesignTimeDbContextFactory<AppDbContext>
└── Migrations/                         # generated migrations + ModelSnapshot

src/Ordinis.Infrastructure.Migrations.PostgreSql/
├── DesignTime/AppDbContextFactory.cs
└── Migrations/
```

Each factory builds `AppDbContext` directly (bypassing `Ordinis.Api/Program.cs` and
`AddInfrastructureServices` entirely) and sets
`.MigrationsAssembly("Ordinis.Infrastructure.Migrations.<Provider>")`.
`InfrastructureServiceExtensions.AddDatabase` sets the matching `MigrationsAssembly` at runtime so
each provider only ever applies its own migration set. `Ordinis.Api` has a `ProjectReference` to
both satellite projects — not used by any code, but required so both migration DLLs land in the
publish output for the runtime by-name assembly load to resolve.

**Connection string resolution.** Each factory resolves `ConnectionStrings:DefaultConnection` the
same way `Ordinis.Api` does at runtime — `Ordinis.Api`'s User Secrets (referenced by ID, since the
satellite project doesn't own that secrets ID) plus environment variables (so
`ConnectionStrings__DefaultConnection` works too, matching the CI/Docker convention in
[docs/CI_CD.md](CI_CD.md)). If neither source has a value — e.g. a fresh clone with nothing
configured yet — it falls back to a syntactically valid but unreachable placeholder string, so
`migrations add` still works fully offline. In practice this means:

- `dotnet ef database update` with **no** `--connection` flag just works against whichever single
  provider you currently have configured via `dotnet user-secrets set` (see
  [docs/LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)).
- Applying to **both** providers in the same sitting (e.g. testing dual-provider support locally)
  still needs an explicit `--connection` per command, since User Secrets only ever holds one
  `DefaultConnection` value at a time — `--connection` overrides whatever the factory resolved, so
  neither invocation touches or depends on the other. See
  [Applying migrations](#applying-migrations) below for exactly this case.

## Adding a migration

Whenever an entity or an `IEntityTypeConfiguration<T>` changes, generate a migration for **both**
providers, in the same commit — a schema change applied to only one provider lets that database
silently drift out of sync with the model until something breaks at runtime.

**Always target the migrations project itself as `--startup-project`, not `Ordinis.Api`.**
`Ordinis.Api` references both satellite projects (for the runtime reason above), so if it's used
as the startup project, `dotnet ef` finds *two* `IDesignTimeDbContextFactory<AppDbContext>`
implementations in its build output and can resolve the wrong one — this happened during initial
setup: asking for the PostgreSQL migration while `Ordinis.Api` was the startup project silently
built the SQL Server one instead, because it matched `appsettings.json`'s default
`DatabaseProvider`.

```sh
dotnet ef migrations add <Name> \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --context AppDbContext

dotnet ef migrations add <Name> \
  --project src/Ordinis.Infrastructure.Migrations.PostgreSql \
  --startup-project src/Ordinis.Infrastructure.Migrations.PostgreSql \
  --context AppDbContext
```

`dotnet ef` requires `Microsoft.EntityFrameworkCore.Design` on whatever `--startup-project` you
pass. Both satellite projects already reference it; if you ever need `Ordinis.Api` as the
startup project for something else, it also carries the package (`PrivateAssets="all"` —
design-time only, not shipped in the published app).

## Reviewing a generated migration before committing

Open the generated `<timestamp>_<Name>.cs` `Up`/`Down` methods for **both** providers and diff
them mentally against the entity config change you made:

- Any concurrency-token property (`RowVersion` on aggregate roots) must render as a plain
  persisted column — `varbinary(max)` (SQL Server) / `bytea` (PostgreSQL) — never a
  computed/DB-generated one. This project uses an **app-managed** concurrency token
  (`AppDbContext.SaveChangesAsync` → `SetConcurrencyTokens()` assigns a fresh
  `Guid.CreateVersion7().ToByteArray()` on every insert/update) specifically so the same
  `.IsConcurrencyToken()` mapping behaves identically on both providers. Native
  `.IsRowVersion()` does not: SQL Server maps it to a DB-generated `rowversion` column, but
  Npgsql only supports `.IsRowVersion()` on a `uint` mapped to the PostgreSQL `xmin` system
  column — applying it to `byte[]` compiles but silently never updates under PostgreSQL,
  defeating optimistic concurrency for that provider only. If you add a new aggregate root,
  reuse the existing pattern (`.IsConcurrencyToken()`, not `.IsRowVersion()`).
- String/decimal columns should get explicit `HasMaxLength()` / `HasPrecision()` in the entity
  config rather than relying on provider defaults, so the two generated schemas don't quietly
  diverge (e.g. SQL Server's default `nvarchar(max)` vs. Postgres' unbounded `text`).
- EF's query-filter warnings at generation time (*"Entity 'Project' has a global query filter
  defined and is the required end of a relationship with..."*) are expected given this project's
  soft-delete filters (`Project`, `ProjectTask`, `Comment`) and are not a sign something broke —
  they're just EF flagging that a soft-deleted parent can dangle a required child reference.

## Common pitfalls

1. **Don't merge the two providers' migrations into one assembly/folder.** One `ModelSnapshot`
   per `DbContext` per assembly is a hard EF Core limit, not a style preference.
2. **Don't use `Ordinis.Api` as `--startup-project`** when generating or listing migrations — see
   above; use the migrations project itself.
3. **Never hand-edit a generated migration.** If it's wrong, `dotnet ef migrations remove` (only
   safe if it hasn't been applied anywhere yet), fix the entity configuration, and regenerate.
4. **Never edit a migration that's already been applied** to any shared environment (another
   dev's machine, staging, prod) — add a new migration instead. Editing history breaks anyone
   who already has that migration recorded in their `__EFMigrationsHistory` table.
5. **Review the actual SQL before applying to a real database**, per provider:
   ```sh
   dotnet ef migrations script \
     --project src/Ordinis.Infrastructure.Migrations.SqlServer \
     --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
   ```

## Applying migrations

This project does **not** call `Database.Migrate()` at startup — that's deliberate. Auto-applying
schema changes on app boot couples deployment/scaling to a schema migration race and makes
rollbacks harder to reason about. Apply migrations as an explicit step instead.

**Single provider, using whatever's already in User Secrets** (see the connection-string
resolution note above — no `--connection` needed):

```sh
dotnet ef database update \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
```

**Both providers locally in one sitting** (e.g. verifying dual-provider support end to end) —
start both Docker containers first, then pass an explicit `--connection` per command so neither
touches User Secrets:

```sh
docker-compose --profile sqlserver up -d db-sqlserver
docker-compose --profile postgres up -d db-postgres

dotnet ef database update \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --connection "Server=localhost,1433;Database=Ordinis;User Id=sa;Password=<SA_PASSWORD from .env>;TrustServerCertificate=True;"

dotnet ef database update \
  --project src/Ordinis.Infrastructure.Migrations.PostgreSql \
  --startup-project src/Ordinis.Infrastructure.Migrations.PostgreSql \
  --connection "Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=<POSTGRES_PASSWORD from .env>;"
```

Both were verified end to end this way: 10 tables created on each provider
(`Organizations`, `Projects`, `Boards`, `Tasks`, `Comments`, `Attachments`, `Users`,
`ProjectMembers`, `OutboxMessages`, `__EFMigrationsHistory`), with `RowVersion` landing as a plain
persisted column — `varbinary` on SQL Server, `bytea` on PostgreSQL — confirming the app-managed
concurrency token (not a DB-generated one) on both.

> **Docker healthcheck gotcha hit while doing this:** `docker-compose.yml`'s SQL Server
> healthcheck originally pointed at `/opt/mssql-tools18/bin/sqlcmd`, but the
> `mcr.microsoft.com/mssql/server:2022-latest` image only ships `/opt/mssql-tools/bin/sqlcmd` (no
> `-tools18` variant) — the healthcheck failed every 10s indefinitely even though the database
> engine itself was fully up (`docker logs` showed `Recovery is complete`). Fixed in
> `docker-compose.yml`; if `docker-compose ps` ever shows `db-sqlserver` stuck `unhealthy` again,
> check `docker inspect --format='{{json .State.Health}}' <container>` for the exact failing
> command before assuming the database itself is broken.

In CI/CD, prefer generating the idempotent SQL script (`migrations script --idempotent`) and
running that against the target database as its own pipeline step, rather than invoking
`dotnet ef database update` directly against production — see
[docs/CI_CD.md](CI_CD.md#applying-migrations-to-that-database).

## Troubleshooting & recovery

### First, figure out what state you're actually in

```sh
dotnet ef migrations list \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
```

Each migration is listed as either applied (no marker) or `(Pending)`. Run this for **both**
providers before doing anything else — the two most common "something's wrong" states are (a) a
migration is pending on one provider but applied on the other, or (b) the DB has migrations
applied that no longer exist as files (you deleted/renamed them without rolling back first). Both
are visible immediately from this output; don't guess from memory.

### Rolling back the most recent migration (already applied, not yet shared)

Two-step: un-apply the DB change, then delete the migration files. Do this **per provider**, and
only if the migration hasn't been applied anywhere but your own machine — see
[Common pitfalls](#common-pitfalls) #4 for why editing/removing an already-shared migration is a
different, worse problem.

```sh
# 1. Revert the database to the previous migration (Down() runs)
dotnet ef database update <PreviousMigrationName> \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer

# 2. Now safe to delete the migration's files
dotnet ef migrations remove \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
```

`migrations remove` refuses to run (with a clear error) if the migration it would delete is still
applied to the database it can reach — that's what step 1 clears. If you can't reach the database
right now (e.g. it's down), fix the entity config and add a **new, corrective** migration instead
of trying to force-remove.

Repeat both steps for `Ordinis.Infrastructure.Migrations.PostgreSql` — a rollback on only one
provider is exactly the kind of drift called out in [Adding a migration](#adding-a-migration).

### Un-applying everything (back to an empty schema, keeping the database itself)

```sh
dotnet ef database update 0 \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer
```

`0` is a special target meaning "before the first migration" — every `Down()` runs in reverse
order, including dropping every table. The database itself, its users/roles, and any
non-EF-managed objects are untouched. Useful when you want a clean slate without recreating the
Docker container. Run the equivalent for PostgreSql. Re-apply with a plain `dotnet ef database
update` (no target) once you're ready.

### A migration failed partway through `database update`

Check `dotnet ef migrations list` first (above) — if the failed migration still shows
`(Pending)`, nothing was recorded as applied and you're safe to fix the root cause and re-run.
Whether the *schema itself* partially changed depends on the provider:

- **SQL Server**: DDL is transactional. A failed migration's changes roll back automatically —
  the schema is left exactly as it was before the attempt.
- **PostgreSQL**: also transactional for the vast majority of DDL this project generates (plain
  `CREATE TABLE`/`ALTER TABLE`/index operations). The one thing that *can't* run inside a
  transaction is `CREATE INDEX CONCURRENTLY` — this project doesn't currently generate that, but
  if you ever hand-author a migration that does, a failure partway through can leave an invalid
  index behind (`DROP INDEX CONCURRENTLY IF EXISTS <name>` cleans it up).

If in doubt, don't guess — connect with `psql`/`sqlcmd` (or pgAdmin, once updated — see
[docs/LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)) and compare what's actually in the database
against the migration's `Up()` method before deciding whether to retry, roll back, or fix forward.

### Starting a single local database completely from scratch

For a Docker-based local dev database where you don't care about existing data, this is faster
and more certain than rolling back migration-by-migration:

```sh
docker-compose --profile sqlserver down -v   # ⚠️ deletes the db-sqlserver-data volume
docker-compose --profile sqlserver up -d db-sqlserver
# wait for healthy — see docs/LOCAL_DEVELOPMENT.md
dotnet ef database update \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --connection "Server=localhost,1433;Database=Ordinis;User Id=sa;Password=<SA_PASSWORD from .env>;TrustServerCertificate=True;"
```

Same pattern with `--profile postgres`/`db-postgres` for PostgreSQL.

> **`-v` destroys the named volume — never run `down -v` against anything containing data you
> care about.** Without `-v`, `docker-compose down` stops the container but keeps its volume; the
> database (and any pending un-rolled-back migrations) survives a plain `down`/`up`.

### Regenerating the entire migration history from scratch (squashing)

Only ever do this while the project is still pre-release/local-only — the moment any migration
has been applied anywhere you don't control (a teammate's machine, staging, prod), this becomes
the "never edit a shared migration" problem from [Common pitfalls](#common-pitfalls) #4 instead,
and the right move is a new corrective migration, not a rewrite of history.

For a genuinely local, nothing-shared-yet reset (e.g. squashing many iterative migrations made
during early development into one clean baseline):

1. Delete every file under `Migrations/` in **both** satellite projects (the migration `.cs`/
   `.Designer.cs` pairs and the `AppDbContextModelSnapshot.cs`).
2. Regenerate a single baseline migration for each provider:

   ```sh
   dotnet ef migrations add InitialCreate \
     --project src/Ordinis.Infrastructure.Migrations.SqlServer \
     --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer

   dotnet ef migrations add InitialCreate \
     --project src/Ordinis.Infrastructure.Migrations.PostgreSql \
     --startup-project src/Ordinis.Infrastructure.Migrations.PostgreSql
   ```

3. Every existing local database now has `__EFMigrationsHistory` rows referencing migration IDs
   that no longer exist as files — `dotnet ef database update` won't reconcile that on its own.
   Reset each database per [Starting a single local database completely from scratch](#starting-a-single-local-database-completely-from-scratch)
   above, then re-apply the new baseline.
4. Review the freshly generated migration for both providers per
   [Reviewing a generated migration before committing](#reviewing-a-generated-migration-before-committing)
   — a squash is still a migration and deserves the same review as any other.

### Quick reference

| Symptom | Command |
| --- | --- |
| Not sure what's applied vs. pending | `dotnet ef migrations list --project <proj> --startup-project <proj>` |
| Undo the last migration (local only) | `database update <PreviousName>` then `migrations remove` |
| Wipe schema, keep the database | `dotnet ef database update 0` |
| Wipe everything, fresh container | `docker-compose --profile <p> down -v` then `up -d` then `database update` |
| Squash all migrations into one baseline | delete `Migrations/`, regenerate `InitialCreate`, reset DB, re-apply |

Whatever the scenario, do it for **both** `Ordinis.Infrastructure.Migrations.SqlServer` and
`Ordinis.Infrastructure.Migrations.PostgreSql` — a recovery step applied to only one provider just
creates the same kind of drift a bad migration would have.
