<#
.SYNOPSIS
Starts a fresh local dev environment end to end: wipes any existing DB
container/volume, brings up a clean database, applies all migrations, and
launches the API on the host (see docs/LOCAL_DEVELOPMENT.md and
docs/MIGRATIONS.md for the manual version of these steps).

.PARAMETER Provider
Either 'sqlserver' (default) or 'postgres'. Must match what's configured in
User Secrets (DatabaseProvider) - the script fixes that up for you unless
-SkipUserSecrets is passed.

.PARAMETER SkipFresh
Don't run `docker-compose down -v` first - reuse whatever container/volume
already exists instead of wiping it. Without this switch the script always
starts from an empty database, per "from scratch".

.PARAMETER SkipUserSecrets
Don't touch `dotnet user-secrets` for Ordinis.Api. Use this if you manage
your own connection string / DatabaseProvider secret already.

.PARAMETER SkipRun
Stop after migrations are applied - don't launch `dotnet run`. Useful if you
just want a ready-to-use database (e.g. to run integration tests against, or
to launch the API from your IDE/debugger instead).

.EXAMPLE
scripts/dev-up.ps1

.EXAMPLE
scripts/dev-up.ps1 -Provider postgres

.EXAMPLE
scripts/dev-up.ps1 -SkipFresh -SkipRun
#>
param(
    [ValidateSet('sqlserver', 'postgres')]
    [string]$Provider = 'sqlserver',

    [switch]$SkipFresh,
    [switch]$SkipUserSecrets,
    [switch]$SkipRun
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot
try {
    $EnvFile = Join-Path $RepoRoot '.env'
    if (-not (Test-Path $EnvFile)) {
        Write-Error "Missing $EnvFile - see docs/LOCAL_DEVELOPMENT.md's '.env file' section."
    }

    function Get-EnvValue {
        param([string]$Name)
        $line = Get-Content $EnvFile | Where-Object { $_ -match "^$Name=" } | Select-Object -First 1
        if (-not $line) { return $null }
        return ($line -replace "^$Name=", '').Trim()
    }

    switch ($Provider) {
        'sqlserver' {
            $ServiceName   = 'db-sqlserver'
            $ContainerName = 'projectmanagementapi-db-sqlserver-1'
            $Password = Get-EnvValue 'SA_PASSWORD'
            if (-not $Password) { Write-Error "SA_PASSWORD not set in $EnvFile" }
            $Connection = "Server=localhost,1433;Database=Ordinis;User Id=sa;Password=$Password;TrustServerCertificate=True;"
            $DatabaseProviderValue = 'SqlServer'
        }
        'postgres' {
            $ServiceName   = 'db-postgres'
            $ContainerName = 'projectmanagementapi-db-postgres-1'
            $Password = Get-EnvValue 'POSTGRES_PASSWORD'
            if (-not $Password) { Write-Error "POSTGRES_PASSWORD not set in $EnvFile" }
            $Connection = "Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=$Password;"
            $DatabaseProviderValue = 'PostgreSQL'
        }
    }

    if (-not $SkipFresh) {
        Write-Host "==> Tearing down any existing containers/volumes (docker-compose down -v)" -ForegroundColor Cyan
        docker-compose down -v
    }

    Write-Host "==> Starting $ServiceName" -ForegroundColor Cyan
    docker-compose --profile $Provider up -d $ServiceName

    Write-Host "==> Waiting for $ContainerName to report healthy" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds(90)
    $status = ''
    while ((Get-Date) -lt $deadline) {
        $status = (docker inspect --format='{{.State.Health.Status}}' $ContainerName 2>$null)
        if ($status -eq 'healthy') { break }
        Start-Sleep -Seconds 2
    }
    if ($status -ne 'healthy') {
        Write-Error "$ContainerName did not become healthy within 90s (last status: '$status'). Check 'docker logs $ContainerName'."
    }
    Write-Host "    $ContainerName is healthy" -ForegroundColor Green

    if (-not $SkipUserSecrets) {
        Write-Host "==> Setting User Secrets for Ordinis.Api (connection string + DatabaseProvider)" -ForegroundColor Cyan
        dotnet user-secrets set "ConnectionStrings:DefaultConnection" $Connection --project src/Ordinis.Api | Out-Null
        dotnet user-secrets set "DatabaseProvider" $DatabaseProviderValue --project src/Ordinis.Api | Out-Null
    }

    Write-Host "==> Applying migrations (dotnet ef database update)" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'ef-migrate.ps1') $Provider database update
    if ($LASTEXITCODE -ne 0) { Write-Error "Migration failed (exit code $LASTEXITCODE)." }

    if ($SkipRun) {
        Write-Host "==> Database ready. Skipping app launch (-SkipRun)." -ForegroundColor Green
        return
    }

    Write-Host "==> Starting Ordinis.Api (http profile - http://localhost:5084)" -ForegroundColor Cyan
    dotnet run --project src/Ordinis.Api --launch-profile http
}
finally {
    Pop-Location
}
