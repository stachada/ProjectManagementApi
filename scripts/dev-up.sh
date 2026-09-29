#!/usr/bin/env bash
# Starts a fresh local dev environment end to end: wipes any existing DB
# container/volume, brings up a clean database, applies all migrations, and
# launches the API on the host (see docs/LOCAL_DEVELOPMENT.md and
# docs/MIGRATIONS.md for the manual version of these steps).
#
# Usage:
#   scripts/dev-up.sh [sqlserver|postgres] [--skip-fresh] [--skip-user-secrets] [--skip-run]
#
# Examples:
#   scripts/dev-up.sh                       # SQL Server, full from-scratch flow, ends with dotnet run
#   scripts/dev-up.sh postgres              # same, but PostgreSQL
#   scripts/dev-up.sh sqlserver --skip-fresh --skip-run   # reuse existing DB, just re-apply migrations
#
# --skip-fresh:        don't run `docker-compose down -v` first - reuse whatever
#                       container/volume already exists instead of wiping it.
# --skip-user-secrets: don't touch `dotnet user-secrets` for Ordinis.Api.
# --skip-run:          stop after migrations are applied - don't launch `dotnet run`.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

PROVIDER="sqlserver"
SKIP_FRESH=false
SKIP_USER_SECRETS=false
SKIP_RUN=false

for arg in "$@"; do
  case "$arg" in
    sqlserver|postgres) PROVIDER="$arg" ;;
    --skip-fresh) SKIP_FRESH=true ;;
    --skip-user-secrets) SKIP_USER_SECRETS=true ;;
    --skip-run) SKIP_RUN=true ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [sqlserver|postgres] [--skip-fresh] [--skip-user-secrets] [--skip-run]" >&2
      exit 1
      ;;
  esac
done

ENV_FILE="$REPO_ROOT/.env"
if [[ ! -f "$ENV_FILE" ]]; then
  echo "Missing $ENV_FILE — see docs/LOCAL_DEVELOPMENT.md's '.env file' section." >&2
  exit 1
fi

case "$PROVIDER" in
  sqlserver)
    SERVICE_NAME="db-sqlserver"
    CONTAINER_NAME="projectmanagementapi-db-sqlserver-1"
    PASSWORD="$(grep -E '^SA_PASSWORD=' "$ENV_FILE" | cut -d '=' -f2-)"
    if [[ -z "$PASSWORD" ]]; then
      echo "SA_PASSWORD not set in $ENV_FILE" >&2
      exit 1
    fi
    CONNECTION="Server=localhost,1433;Database=Ordinis;User Id=sa;Password=${PASSWORD};TrustServerCertificate=True;"
    DATABASE_PROVIDER_VALUE="SqlServer"
    ;;
  postgres)
    SERVICE_NAME="db-postgres"
    CONTAINER_NAME="projectmanagementapi-db-postgres-1"
    PASSWORD="$(grep -E '^POSTGRES_PASSWORD=' "$ENV_FILE" | cut -d '=' -f2-)"
    if [[ -z "$PASSWORD" ]]; then
      echo "POSTGRES_PASSWORD not set in $ENV_FILE" >&2
      exit 1
    fi
    CONNECTION="Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=${PASSWORD};"
    DATABASE_PROVIDER_VALUE="PostgreSQL"
    ;;
esac

if [[ "$SKIP_FRESH" == "false" ]]; then
  echo "==> Tearing down any existing containers/volumes (docker-compose down -v)"
  docker-compose down -v
fi

echo "==> Starting $SERVICE_NAME"
docker-compose --profile "$PROVIDER" up -d "$SERVICE_NAME"

echo "==> Waiting for $CONTAINER_NAME to report healthy"
deadline=$((SECONDS + 90))
status=""
while [[ $SECONDS -lt $deadline ]]; do
  status="$(docker inspect --format='{{.State.Health.Status}}' "$CONTAINER_NAME" 2>/dev/null || true)"
  [[ "$status" == "healthy" ]] && break
  sleep 2
done
if [[ "$status" != "healthy" ]]; then
  echo "$CONTAINER_NAME did not become healthy within 90s (last status: '$status'). Check 'docker logs $CONTAINER_NAME'." >&2
  exit 1
fi
echo "    $CONTAINER_NAME is healthy"

if [[ "$SKIP_USER_SECRETS" == "false" ]]; then
  echo "==> Setting User Secrets for Ordinis.Api (connection string + DatabaseProvider)"
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "$CONNECTION" --project src/Ordinis.Api >/dev/null
  dotnet user-secrets set "DatabaseProvider" "$DATABASE_PROVIDER_VALUE" --project src/Ordinis.Api >/dev/null
fi

echo "==> Applying migrations (dotnet ef database update)"
"$REPO_ROOT/scripts/ef-migrate.sh" "$PROVIDER" database update

if [[ "$SKIP_RUN" == "true" ]]; then
  echo "==> Database ready. Skipping app launch (--skip-run)."
  exit 0
fi

echo "==> Starting Ordinis.Api (http profile - http://localhost:5084)"
exec dotnet run --project src/Ordinis.Api --launch-profile http
