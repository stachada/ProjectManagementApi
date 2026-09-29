#!/usr/bin/env bash
# Wrapper around `dotnet ef` for the dual-provider migrations satellite projects
# (see docs/MIGRATIONS.md). `dotnet ef` resolves its connection string the same way
# Ordinis.Api does at runtime — User Secrets / env vars, which hold exactly one
# DefaultConnection value at a time. Checking or applying migrations against
# *whichever provider you're not currently pointed at* (e.g. verifying dual-provider
# support, or just checking status on the other DB) needs an explicit --connection
# per docs/MIGRATIONS.md#applying-migrations. This script builds that connection
# string from .env so you don't have to retype it.
#
# Usage:
#   scripts/ef-migrate.sh <sqlserver|postgres> <dotnet-ef-args...>
#
# Examples:
#   scripts/ef-migrate.sh sqlserver migrations list
#   scripts/ef-migrate.sh postgres  migrations list
#   scripts/ef-migrate.sh postgres  database update
#   scripts/ef-migrate.sh sqlserver migrations add AddSomething
#   scripts/ef-migrate.sh postgres  migrations script --idempotent
#
# Assumes the corresponding docker-compose database (db-sqlserver / db-postgres) is
# already running and reachable at localhost — this targets the "API runs on the
# host machine" connection strings from docs/LOCAL_DEVELOPMENT.md, not the
# in-container hostnames used by the full-stack-in-Docker profile.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env"

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <sqlserver|postgres> <dotnet-ef-args...>" >&2
  echo "Example: $0 postgres migrations list" >&2
  exit 1
fi

PROVIDER="$1"
shift

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Missing $ENV_FILE — see docs/LOCAL_DEVELOPMENT.md's '.env file' section." >&2
  exit 1
fi

case "$PROVIDER" in
  sqlserver)
    PROJECT="src/Ordinis.Infrastructure.Migrations.SqlServer"
    PASSWORD="$(grep -E '^SA_PASSWORD=' "$ENV_FILE" | cut -d '=' -f2-)"
    if [[ -z "$PASSWORD" ]]; then
      echo "SA_PASSWORD not set in $ENV_FILE" >&2
      exit 1
    fi
    CONNECTION="Server=localhost,1433;Database=Ordinis;User Id=sa;Password=${PASSWORD};TrustServerCertificate=True;"
    ;;
  postgres)
    PROJECT="src/Ordinis.Infrastructure.Migrations.PostgreSql"
    PASSWORD="$(grep -E '^POSTGRES_PASSWORD=' "$ENV_FILE" | cut -d '=' -f2-)"
    if [[ -z "$PASSWORD" ]]; then
      echo "POSTGRES_PASSWORD not set in $ENV_FILE" >&2
      exit 1
    fi
    CONNECTION="Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=${PASSWORD};"
    ;;
  *)
    echo "Unknown provider '$PROVIDER' — expected 'sqlserver' or 'postgres'." >&2
    exit 1
    ;;
esac

# --connection is only a recognized option for commands that actually talk to a live
# database (`database update`/`database drop`, and optionally `migrations list` for
# applied/pending status) — `dotnet ef` rejects it outright ("Unrecognized option") on
# commands that only operate on the model/files, like `migrations add`/`migrations script`.
NEEDS_CONNECTION=false
if [[ "${1-}" == "database" ]] || { [[ "${1-}" == "migrations" ]] && [[ "${2-}" == "list" ]]; }; then
  NEEDS_CONNECTION=true
fi

if [[ "$NEEDS_CONNECTION" == "true" ]]; then
  echo "+ dotnet ef $* --project $PROJECT --startup-project $PROJECT --context AppDbContext --connection <redacted>" >&2
  exec dotnet ef "$@" \
    --project "$PROJECT" \
    --startup-project "$PROJECT" \
    --context AppDbContext \
    --connection "$CONNECTION"
else
  echo "+ dotnet ef $* --project $PROJECT --startup-project $PROJECT --context AppDbContext" >&2
  exec dotnet ef "$@" \
    --project "$PROJECT" \
    --startup-project "$PROJECT" \
    --context AppDbContext
fi
