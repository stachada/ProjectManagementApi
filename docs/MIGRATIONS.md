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

Each factory builds `AppDbContext` directly with a dummy, syntactically-valid connection string
and sets `.MigrationsAssembly("Ordinis.Infrastructure.Migrations.<Provider>")` — this bypasses
`Ordinis.Api/Program.cs` and `AddInfrastructureServices` entirely, so generating a migration
never needs a real connection string or User Secrets. `InfrastructureServiceExtensions.AddDatabase`
sets the matching `MigrationsAssembly` at runtime so each provider only ever applies its own
migration set. `Ordinis.Api` has a `ProjectReference` to both satellite projects — not used by
any code, but required so both migration DLLs land in the publish output for the runtime
by-name assembly load to resolve.

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
rollbacks harder to reason about. Apply migrations as an explicit step instead:

```sh
dotnet ef database update \
  --project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --startup-project src/Ordinis.Infrastructure.Migrations.SqlServer \
  --connection "<real-connection-string>"
```

(swap the project/provider for PostgreSQL). In CI/CD, prefer generating the idempotent SQL script
(`migrations script --idempotent`) and running that against the target database as its own
pipeline step, rather than invoking `dotnet ef database update` directly against production.
