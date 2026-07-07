# Local development setup

Secrets, connection strings, and Docker commands for running the API and its database locally.
See [docs/MIGRATIONS.md](MIGRATIONS.md) for schema/migration commands once the database is up.

## User Secrets

User Secrets store sensitive configuration on your machine only — outside the project directory,
never committed to git. The secrets file for `Ordinis.Api` lives at:

```
%APPDATA%\Microsoft\UserSecrets\7d722b01-cc54-4c3d-9fa1-d572c2451f7e\secrets.json
```

(that GUID is `Ordinis.Api.csproj`'s `<UserSecretsId>` — it's not sensitive, just an ID, and it's
already committed in the `.csproj`; only the *contents* of `secrets.json` are private.)

Set the connection string — pick one provider:

```bash
# SQL Server — use single quotes so Bash does not expand the `!` in the password
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  'Server=localhost,1433;Database=Ordinis;User Id=sa;Password=<password>;TrustServerCertificate=True;' \
  --project src/Ordinis.Api

# PostgreSQL
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  'Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=<password>;' \
  --project src/Ordinis.Api
dotnet user-secrets set "DatabaseProvider" 'PostgreSQL' --project src/Ordinis.Api
```

> **`DatabaseProvider` and `ConnectionStrings:DefaultConnection` must agree.** Switching providers
> means updating both — `InfrastructureServiceExtensions.AddDatabase` validates and normalizes
> `DatabaseProvider` at startup and throws immediately if it doesn't recognize the value, rather
> than failing later on first DB access.

Verify what's set:

```bash
dotnet user-secrets list --project src/Ordinis.Api
```

If `secrets.json` ever gets corrupted (e.g. a `user-secrets set` command was interrupted), the app
throws `System.IO.InvalidDataException: Failed to load configuration` on startup. Fix by writing
`{}` to the file to restore valid empty JSON, then re-run your `user-secrets set` commands.

### JWT signing key

> **Not yet consumed by application code.** `Jwt:SigningKey` / `Jwt__SigningKey` is already wired
> into `docker-compose.yml` and `.github/workflows/ci.yml` as a forward-looking placeholder, but
> no `appsettings.json` or C# code currently reads it — JWT auth is Phase 8 (Security) in
> `BUILD_PLAN.md`, not yet implemented. Setting it now is harmless and saves a step later.

Generate a cryptographically random key (32+ characters; 64 random bytes base64-encoded is the
project's convention) and use the **same value** in User Secrets, `.env`, and the GitHub Actions
secret — tokens signed with one key aren't verifiable with another.

```powershell
# PowerShell — cryptographically secure, not Get-Random
$bytes = [byte[]]::new(64)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

```bash
# OpenSSL (Git Bash / WSL)
openssl rand -base64 64
```

```bash
dotnet user-secrets set "Jwt:SigningKey" '<generated-key>' --project src/Ordinis.Api
```

**Rotating the key** immediately invalidates all existing tokens (no grace period). If the key was
ever exposed — committed to git, logged, shared — rotate it in all three locations and redeploy;
if it was committed to git, it's permanently in history and must be treated as compromised
regardless of the rotation.

## Connection strings by scenario

The hostname depends on *where the API process runs* — the database is always the Docker
container, only the path to reach it differs.

| Scenario | API runs on | DB host to use | Where to set it |
|---|---|---|---|
| SQL Server, `dotnet run` | host machine | `localhost,1433` | User Secrets |
| PostgreSQL, `dotnet run` | host machine | `localhost:5432` | User Secrets |
| SQL Server, `docker-compose` | container | `db-sqlserver` | `.env` → `CONNECTION_STRING` |
| PostgreSQL, `docker-compose` | container | `db-postgres` | `.env` → `CONNECTION_STRING` |

`db-sqlserver` / `db-postgres` are the service names in `docker-compose.yml` — reachable by name
only from inside the Docker network, not from the host.

## `.env` file (Docker Compose)

`docker-compose.yml` reads `.env` from the repo root automatically; it's gitignored.

> **Gap found while writing this doc:** the README and `docker-compose` workflow both say
> "copy `.env.example` to `.env`", but no `.env.example` currently exists in the repo — only a
> local, gitignored `.env`. Worth adding one (see the required keys below) so a fresh clone can
> follow the documented step.

Required keys:

| Variable | Purpose |
|---|---|
| `DATABASE_PROVIDER` | `SqlServer` or `PostgreSQL` — must match `CONNECTION_STRING` |
| `CONNECTION_STRING` | Full ADO.NET connection string, using the `db-sqlserver`/`db-postgres` host from the table above |
| `SA_PASSWORD` | SQL Server SA password — must meet complexity requirements (upper + lower + digit + symbol) |
| `POSTGRES_PASSWORD` | PostgreSQL superuser password |
| `JWT_SIGNING_KEY` | Same value as the `Jwt:SigningKey` User Secret |

## Docker commands

All commands run from the repo root.

**Recommended for local dev** — DB in Docker, API on host (hot reload + debugger support):

```bash
docker-compose --profile sqlserver up db-sqlserver   # or --profile postgres up db-postgres
dotnet run --project src/Ordinis.Api
```

**Full stack in Docker** — API and DB both containers, config from `.env` (User Secrets not used):

```bash
docker-compose --profile sqlserver up --build   # or --profile postgres up --build
```

**Teardown:**

```bash
docker-compose down       # stop containers, keep DB volumes
docker-compose down -v    # stop containers and delete DB volumes (wipes all data)
```

Named volumes (`db-sqlserver-data`, `db-postgres-data`) persist across `docker-compose down`
without `-v`.
