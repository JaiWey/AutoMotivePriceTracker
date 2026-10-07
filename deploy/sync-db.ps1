<#
.SYNOPSIS
  Copies the data in the local (docker compose) PostgreSQL database to the AWS RDS database, then rebuilds
  the cloud search index. The cloud data is REPLACED.

.DESCRIPTION
  1. dumps the local data (data only; the schema is already created by the API's migrations) with pg_dump
  2. opens an SSM port-forward through the API host to the private RDS instance
  3. checks the cloud schema is not ahead of the local one (EF migrations), empties the cloud tables and restores the dump
  4. asks the API on the host to reindex search (POST /api/admin/reindex)

  Needs: AWS CLI + the Session Manager plugin, Docker (pg_dump/psql/pg_restore run in postgres:17 containers,
  so no PostgreSQL install is required), the local compose stack up (`docker compose up -d --wait`) and
  the cloud already deployed (deploy/deploy.ps1) so its schema exists. Asks for confirmation unless -Force.

.PARAMETER Region
  Must match the `region` Terraform variable (default ap-southeast-2).
#>
[CmdletBinding()]
param(
    [string]$Region = 'ap-southeast-2',
    [string]$Name = 'autoprice',
    [string]$LocalContainer = 'autoprice-postgres',
    [string]$LocalUser = 'postgres',
    [string]$LocalDatabase = 'autoprice',
    [int]$TunnelPort = 15432,
    [switch]$SkipReindex,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$tf = Join-Path $root 'infra/terraform'
$pgImage = 'postgres:17'

function Invoke-Native {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-TfOutput([string]$OutputName) {
    $value = & terraform "-chdir=$tf" output -raw $OutputName
    if ($LASTEXITCODE -ne 0) { throw "terraform output $OutputName failed" }
    return $value
}

# psql/pg_restore against the tunnelled cloud database, run in a container (reaches the host's tunnel port
# through host.docker.internal)
function Invoke-Cloud {
    param([string[]]$Arguments, [string]$Mount)
    $docker = @('run', '--rm', '-i', '-e', "PGPASSWORD=$script:cloudPassword", '-e', 'PGSSLMODE=require')
    if ($Mount) { $docker += @('-v', "${Mount}:/dump") }
    $docker += @($pgImage) + $Arguments
    return & docker @docker
}

$cloudArgs = @('--host', 'host.docker.internal', '--port', "$TunnelPort")

# The AWS CLI needs this plugin for port forwarding; without it the session dies immediately
if (-not (Get-Command session-manager-plugin -ErrorAction SilentlyContinue) -and
    -not (Test-Path 'C:\Program Files\Amazon\SessionManagerPlugin\bin\session-manager-plugin.exe')) {
    throw 'The AWS Session Manager plugin is not installed: https://s3.amazonaws.com/session-manager-downloads/plugin/latest/windows/SessionManagerPluginSetup.exe (then open a new terminal).'
}

# The installer's PATH entry is invisible to terminals opened before it ran; make sure the AWS CLI finds it
$pluginDir = 'C:\Program Files\Amazon\SessionManagerPlugin\bin'
if ((Test-Path $pluginDir) -and ($env:PATH -notlike "*$pluginDir*")) { $env:PATH = "$pluginDir;$env:PATH" }

# --- Local side: is it running, and which migration is it on? ---
$running = (& docker inspect -f '{{.State.Running}}' $LocalContainer 2>$null)
if ($running -ne 'true') { throw "Local container '$LocalContainer' is not running (docker compose up -d --wait)." }
$localMigrations = @(& docker exec $LocalContainer psql -U $LocalUser -d $LocalDatabase -At -c 'select "MigrationId" from "__EFMigrationsHistory"')
if ($LASTEXITCODE -ne 0 -or $localMigrations.Count -eq 0) { throw 'Could not read the local migration history (has the API ever run against the local database?).' }

# --- Cloud connection details come from the same SSM parameter the API host uses ---
$connection = & aws ssm get-parameter --name "/$Name/db-connection-string" --with-decryption --query Parameter.Value --output text --region $Region
if ($LASTEXITCODE -ne 0) { throw 'Could not read the connection string from SSM (aws configure? deployed?).' }
$cloudHost = [regex]::Match($connection, 'Host=([^;]+)').Groups[1].Value
$cloudDatabase = [regex]::Match($connection, 'Database=([^;]+)').Groups[1].Value
$cloudUser = [regex]::Match($connection, 'Username=([^;]+)').Groups[1].Value
$script:cloudPassword = [regex]::Match($connection, 'Password=([^;]+)').Groups[1].Value
if (-not ($cloudHost -and $cloudDatabase -and $cloudUser -and $script:cloudPassword)) { throw 'Could not parse the cloud connection string.' }
$cloudArgs += @('--username', $cloudUser, '--dbname', $cloudDatabase)

$instanceId = Get-TfOutput api_instance_id

if (-not $Force) {
    Write-Host "This REPLACES all data in the cloud database '$cloudDatabase' ($cloudHost) with the local '$LocalDatabase'." -ForegroundColor Yellow
    if ((Read-Host 'Type yes to continue') -ne 'yes') { throw 'Cancelled.' }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "autoprice-sync-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$tunnel = $null
try {
    Write-Host '== 1/4 Dump the local database' -ForegroundColor Cyan
    # The migration history table is left alone: the cloud schema is already at its own migration
    Invoke-Native docker @('exec', $LocalContainer, 'pg_dump', '-U', $LocalUser, '-d', $LocalDatabase,
        '--data-only', '--no-owner', '--exclude-table="__EFMigrationsHistory"', '-Fc', '-f', '/tmp/autoprice.dump')
    Invoke-Native docker @('cp', "${LocalContainer}:/tmp/autoprice.dump", (Join-Path $work 'autoprice.dump'))
    & docker exec $LocalContainer rm -f /tmp/autoprice.dump | Out-Null
    Write-Host ("Dump: {0:N1} MB" -f ((Get-Item (Join-Path $work 'autoprice.dump')).Length / 1MB))

    Write-Host '== 2/4 Open the tunnel to RDS' -ForegroundColor Cyan
    $parameters = Join-Path $work 'tunnel.json'
    @{ host = @($cloudHost); portNumber = @('5432'); localPortNumber = @("$TunnelPort") } | ConvertTo-Json -Compress |
        Set-Content -Path $parameters -Encoding ascii
    $tunnel = Start-Process -FilePath aws -PassThru -WindowStyle Hidden -ArgumentList @(
        'ssm', 'start-session', '--target', $instanceId, '--region', $Region,
        '--document-name', 'AWS-StartPortForwardingSessionToRemoteHost', '--parameters', "file://$parameters")
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        Start-Sleep -Seconds 2
        if ($tunnel.HasExited) { throw 'The SSM session ended early (is the Session Manager plugin installed? is the host online?).' }
        $client = [Net.Sockets.TcpClient]::new()
        try { $client.Connect('127.0.0.1', $TunnelPort); $ready = $true } catch { } finally { $client.Dispose() }
    }
    if (-not $ready) { throw "Nothing is listening on localhost:$TunnelPort." }

    Write-Host '== 3/4 Restore into the cloud database' -ForegroundColor Cyan
    $cloudMigration = (Invoke-Cloud (@('psql') + $cloudArgs + @('-At', '-c', 'select max("MigrationId") from "__EFMigrationsHistory"'))) | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or -not $cloudMigration) { throw 'Could not read the cloud migration history (is the API running and migrated?).' }
    # The local history may list migrations that were later dropped from the code, so only require that
    # the cloud's latest migration has been applied locally too
    if ($localMigrations -notcontains $cloudMigration.Trim()) {
        throw "Schema mismatch: the cloud is at '$($cloudMigration.Trim())', which the local database has not applied. Migrate the local database first."
    }

    # Empty every table except the migration history, restarting identities, then load in one transaction
    $truncate = @'
DO $$
DECLARE t text;
BEGIN
  SELECT string_agg(format('%I.%I', schemaname, tablename), ', ') INTO t
  FROM pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory';
  IF t IS NOT NULL THEN EXECUTE 'TRUNCATE TABLE ' || t || ' RESTART IDENTITY CASCADE'; END IF;
END $$;
'@
    Invoke-Cloud (@('psql') + $cloudArgs + @('-v', 'ON_ERROR_STOP=1', '-c', $truncate)) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Truncating the cloud tables failed.' }
    Invoke-Cloud -Mount $work -Arguments (@('pg_restore') + $cloudArgs + @('--data-only', '--no-owner', '--single-transaction', '--exit-on-error', '/dump/autoprice.dump')) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore failed; the transaction was rolled back, but the cloud tables are now empty.' }

    $counts = Invoke-Cloud (@('psql') + $cloudArgs + @('-At', '-c', 'select (select count(*) from "Products") || '' products, '' || (select count(*) from "PriceRecords") || '' price records'''))
    Write-Host "Cloud now has: $counts" -ForegroundColor Green
} finally {
    if ($tunnel -and -not $tunnel.HasExited) { & taskkill /PID $tunnel.Id /T /F | Out-Null }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($SkipReindex) { return }

Write-Host '== 4/4 Rebuild the search index' -ForegroundColor Cyan
$remote = 'S=$(grep ^Origin__SharedSecret= /opt/app/api.env | cut -d= -f2-); curl -fsS -X POST -H "X-Origin-Verify: $S" http://localhost:8080/api/admin/reindex'
$parametersFile = Join-Path ([IO.Path]::GetTempPath()) "autoprice-ssm-$([guid]::NewGuid().ToString('N')).json"
@{ commands = @($remote) } | ConvertTo-Json -Compress | Set-Content -Path $parametersFile -Encoding ascii
try {
    $commandId = & aws ssm send-command --region $Region --instance-ids $instanceId `
        --document-name AWS-RunShellScript --parameters "file://$parametersFile" `
        --timeout-seconds 300 --query Command.CommandId --output text
    if ($LASTEXITCODE -ne 0) { throw 'ssm send-command failed' }
} finally {
    Remove-Item $parametersFile -ErrorAction SilentlyContinue
}
$status = 'InProgress'
while ($status -in 'Pending', 'InProgress', 'Delayed') {
    Start-Sleep -Seconds 5
    $status = & aws ssm get-command-invocation --region $Region --command-id $commandId --instance-id $instanceId --query Status --output text
}
& aws ssm get-command-invocation --region $Region --command-id $commandId --instance-id $instanceId --query '[StandardOutputContent,StandardErrorContent]' --output text
if ($status -ne 'Success') { throw "Reindex finished with status $status" }

Write-Host "`nDone. Reload $(Get-TfOutput site_url)" -ForegroundColor Green
