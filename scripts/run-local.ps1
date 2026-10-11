<#
.SYNOPSIS
  Runs every OrderFlow backend service locally (no Docker required).

.DESCRIPTION
  Requires a PostgreSQL server (default: localhost:5432, postgres/postgres, database "orderflow").
  Without RABBITMQ_URL the services use MassTransit's PostgreSQL transport, so Postgres is the only dependency.
  Set DATABASE_URL / RABBITMQ_URL / REDIS_URL beforehand to run against cloud infrastructure instead.

.EXAMPLE
  ./scripts/run-local.ps1          # start all services (logs in ./.logs)
  ./scripts/run-local.ps1 -Stop    # stop them
#>
param([switch]$Stop)

$root = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $root '.logs'
$pidFile = Join-Path $logs 'pids.txt'

if ($Stop) {
    if (Test-Path $pidFile) {
        Get-Content $pidFile | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
        Remove-Item $pidFile
    }
    Write-Host 'Stopped OrderFlow services.'
    return
}

New-Item -ItemType Directory -Force $logs | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'

Write-Host 'Building solution...'
dotnet build (Join-Path $root 'OrderFlow.slnx') -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$services = @(
    @{ Name = 'catalog';      Path = 'src/Services/Catalog/OrderFlow.Catalog.API' },
    @{ Name = 'ordering';     Path = 'src/Services/Ordering/OrderFlow.Ordering.API' },
    @{ Name = 'payment';      Path = 'src/Services/Payment/OrderFlow.Payment.Worker' },
    @{ Name = 'notification'; Path = 'src/Services/Notification/OrderFlow.Notification.Worker' },
    @{ Name = 'gateway';      Path = 'src/Gateway/OrderFlow.Gateway' }
)

$pids = foreach ($s in $services) {
    $project = Join-Path $root $s.Path
    $p = Start-Process dotnet -ArgumentList @('run', '--no-build', '--project', "`"$project`"", '--launch-profile', 'http') `
        -RedirectStandardOutput (Join-Path $logs "$($s.Name).log") `
        -RedirectStandardError (Join-Path $logs "$($s.Name).err.log") `
        -WindowStyle Hidden -PassThru
    Write-Host ("Started {0,-13} pid {1}" -f $s.Name, $p.Id)
    $p.Id
    Start-Sleep -Seconds 2
}
$pids | Set-Content $pidFile

Write-Host ''
Write-Host 'Gateway:  http://localhost:5000  (API docs at /docs on each service)'
Write-Host 'Frontend: cd frontend; npm start  ->  http://localhost:4200'
Write-Host "Logs:     $logs"
