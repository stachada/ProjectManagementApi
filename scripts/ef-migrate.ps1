<#
.SYNOPSIS
Wrapper around `dotnet ef` for the dual-provider migrations satellite projects
(see docs/MIGRATIONS.md). PowerShell equivalent of ef-migrate.sh.

`dotnet ef` resolves its connection string the same way Ordinis.Api does at
runtime - User Secrets / env vars, which hold exactly one DefaultConnection value
at a time. Checking or applying migrations against *whichever provider you're not
currently pointed at* (e.g. verifying dual-provider support, or just checking
status on the other DB) needs an explicit --connection per
docs/MIGRATIONS.md#applying-migrations. This script builds that connection string
from .env so you don't have to retype it.

.PARAMETER Provider
Either 'sqlserver' or 'postgres'.

.PARAMETER EfArgs
The dotnet ef subcommand and its arguments, e.g. 'migrations', 'list'.

.EXAMPLE
scripts/ef-migrate.ps1 sqlserver migrations list

.EXAMPLE
scripts/ef-migrate.ps1 postgres database update

.EXAMPLE
scripts/ef-migrate.ps1 sqlserver migrations add AddSomething

.EXAMPLE
scripts/ef-migrate.ps1 postgres migrations script --idempotent

.NOTES
Assumes the corresponding docker-compose database (db-sqlserver / db-postgres) is
already running and reachable at localhost - this targets the "API runs on the
host machine" connection strings from docs/LOCAL_DEVELOPMENT.md, not the
in-container hostnames used by the full-stack-in-Docker profile.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('sqlserver', 'postgres')]
    [string]$Provider,

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$EfArgs
)

$ErrorActionPreference = 'Stop'

if (-not $EfArgs -or $EfArgs.Count -eq 0) {
    Write-Error "Usage: scripts/ef-migrate.ps1 <sqlserver|postgres> <dotnet-ef-args...>`nExample: scripts/ef-migrate.ps1 postgres migrations list"
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
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
        $Project = 'src/Ordinis.Infrastructure.Migrations.SqlServer'
        $Password = Get-EnvValue 'SA_PASSWORD'
        if (-not $Password) { Write-Error "SA_PASSWORD not set in $EnvFile" }
        $Connection = "Server=localhost,1433;Database=Ordinis;User Id=sa;Password=$Password;TrustServerCertificate=True;"
    }
    'postgres' {
        $Project = 'src/Ordinis.Infrastructure.Migrations.PostgreSql'
        $Password = Get-EnvValue 'POSTGRES_PASSWORD'
        if (-not $Password) { Write-Error "POSTGRES_PASSWORD not set in $EnvFile" }
        $Connection = "Host=localhost;Port=5432;Database=Ordinis;Username=ordinis;Password=$Password;"
    }
}

# --connection is only a recognized option for commands that actually talk to a live
# database (`database update`/`database drop`, and optionally `migrations list` for
# applied/pending status) - `dotnet ef` rejects it outright ("Unrecognized option") on
# commands that only operate on the model/files, like `migrations add`/`migrations script`.
$NeedsConnection = ($EfArgs[0] -eq 'database') -or (($EfArgs[0] -eq 'migrations') -and ($EfArgs[1] -eq 'list'))

if ($NeedsConnection) {
    Write-Host "+ dotnet ef $($EfArgs -join ' ') --project $Project --startup-project $Project --context AppDbContext --connection <redacted>"
    & dotnet ef @EfArgs --project $Project --startup-project $Project --context AppDbContext --connection $Connection
}
else {
    Write-Host "+ dotnet ef $($EfArgs -join ' ') --project $Project --startup-project $Project --context AppDbContext"
    & dotnet ef @EfArgs --project $Project --startup-project $Project --context AppDbContext
}
exit $LASTEXITCODE
