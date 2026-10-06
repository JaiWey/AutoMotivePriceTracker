<#
.SYNOPSIS
  Builds and deploys AutoMotivePriceTracker to AWS (infrastructure in infra/terraform).

.DESCRIPTION
  1. terraform apply for the SSR ECR repository only (the Lambda needs its image to exist before it is created)
  2. publishes the API as a self-contained linux-x64 build (no Docker on the host), and builds and pushes the
     Angular SSR (Lambda) image
  3. terraform apply for everything else
  4. syncs the Angular static files to S3, updates the Lambda image, uploads the API build to S3,
     runs /opt/app/deploy.sh on the API host through SSM, and invalidates CloudFront

  Both terraform applies ask for confirmation unless -AutoApprove is passed. Safe to re-run for every release.

.PARAMETER Region
  Must match the `region` Terraform variable (default ap-southeast-2).
#>
[CmdletBinding()]
param(
    [string]$Region = 'ap-southeast-2',
    [string]$Name = 'autoprice',
    [switch]$AutoApprove,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$tf = Join-Path $root 'infra/terraform'
$approve = if ($AutoApprove) { @('-auto-approve') } else { @() }

function Invoke-Native {
    # Runs a native command and fails the script when it exits non-zero.
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-TfOutput([string]$OutputName) {
    $value = & terraform "-chdir=$tf" output -raw $OutputName
    if ($LASTEXITCODE -ne 0) { throw "terraform output $OutputName failed" }
    return $value
}

$account = (& aws sts get-caller-identity --query Account --output text)
if ($LASTEXITCODE -ne 0) { throw 'AWS credentials are not configured (aws configure).' }
$registry = "$account.dkr.ecr.$Region.amazonaws.com"
$ssrImage = "$registry/$Name-ssr:latest"

Write-Host '== 1/4 ECR repositories' -ForegroundColor Cyan
Invoke-Native terraform (@("-chdir=$tf", 'init', '-input=false'))
Invoke-Native terraform (@("-chdir=$tf", 'apply', '-target=aws_ecr_repository.ssr') + $approve)

if (-not $SkipBuild) {
    Write-Host '== 2/4 Build and push images' -ForegroundColor Cyan
    $password = & aws ecr get-login-password --region $Region
    if ($LASTEXITCODE -ne 0) { throw 'ecr get-login-password failed' }
    $password | & docker login --username AWS --password-stdin $registry
    if ($LASTEXITCODE -ne 0) { throw 'docker login failed' }

    Invoke-Native docker @('build', '--platform', 'linux/amd64', '--provenance=false', '--sbom=false', '-t', $ssrImage, (Join-Path $root 'src/AutoMotivePriceTracker.Client'))
    Invoke-Native docker @('push', $ssrImage)
}

# The API host runs the build directly under systemd; self-contained, so it needs no .NET runtime installed
$apiArchive = Join-Path ([IO.Path]::GetTempPath()) 'autoprice-api.tar.gz'
if (-not $SkipBuild) {
    Write-Host 'Publishing the API (linux-x64, self-contained)' -ForegroundColor Cyan
    $publishDir = Join-Path ([IO.Path]::GetTempPath()) "autoprice-api-$([guid]::NewGuid().ToString('N'))"
    try {
        Invoke-Native dotnet @('publish', (Join-Path $root 'src/AutoMotivePriceTracker.Web'), '-c', 'Release', '-r', 'linux-x64', '--self-contained', '-o', $publishDir)
        # Scraping is off in the cloud, so skip the 200 MB Playwright driver bundled in the publish output
        Remove-Item -Recurse -Force (Join-Path $publishDir '.playwright') -ErrorAction SilentlyContinue
        Invoke-Native tar @('-czf', $apiArchive, '-C', $publishDir, '.')
    } finally {
        Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
    }
}
if (-not (Test-Path $apiArchive)) { throw "No API build at $apiArchive (run without -SkipBuild first)." }

Write-Host '== 3/4 Infrastructure' -ForegroundColor Cyan
Invoke-Native terraform (@("-chdir=$tf", 'apply') + $approve)

Write-Host '== 4/4 Release' -ForegroundColor Cyan
$assetsBucket = Get-TfOutput assets_bucket
$artifactsBucket = Get-TfOutput artifacts_bucket
$lambdaName = Get-TfOutput ssr_lambda_name
$instanceId = Get-TfOutput api_instance_id
$distributionId = Get-TfOutput cloudfront_distribution_id
$siteUrl = Get-TfOutput site_url

# Static files come out of the SSR image so they match the server bundle exactly
$tmp = Join-Path ([IO.Path]::GetTempPath()) "autoprice-browser-$([guid]::NewGuid().ToString('N'))"
$container = (& docker create $ssrImage).Trim()
try {
    Invoke-Native docker @('cp', "${container}:/app/dist/AutoMotivePriceTracker.Client/browser", $tmp)
} finally {
    & docker rm $container | Out-Null
}
Invoke-Native aws @('s3', 'sync', $tmp, "s3://$assetsBucket", '--delete', '--exclude', 'index.csr.html', '--region', $Region)
Remove-Item -Recurse -Force $tmp

# Point the Lambda at the image just pushed (a no-op on the very first deploy)
Invoke-Native aws @('lambda', 'update-function-code', '--function-name', $lambdaName, '--image-uri', $ssrImage, '--region', $Region, '--output', 'text', '--query', 'LastUpdateStatus')
Invoke-Native aws @('lambda', 'wait', 'function-updated', '--function-name', $lambdaName, '--region', $Region)

Invoke-Native aws @('s3', 'cp', $apiArchive, "s3://$artifactsBucket/api.tar.gz", '--region', $Region)

# Wait for the SSM agent (a freshly created instance needs a minute or two), then run the host's deploy script
Write-Host 'Waiting for the API host to register with SSM...'
for ($i = 0; $i -lt 40; $i++) {
    $online = & aws ssm describe-instance-information --region $Region --filters "Key=InstanceIds,Values=$instanceId" --query 'InstanceInformationList[0].PingStatus' --output text
    if ($online -eq 'Online') { break }
    Start-Sleep -Seconds 15
}
if ($online -ne 'Online') { throw "Instance $instanceId did not come online in SSM." }

$remote = 'for i in $(seq 1 60); do [ -x /opt/app/deploy.sh ] && break; sleep 10; done; /opt/app/deploy.sh'
$parametersFile = Join-Path ([IO.Path]::GetTempPath()) "autoprice-ssm-$([guid]::NewGuid().ToString('N')).json"
@{ commands = @($remote) } | ConvertTo-Json -Compress | Set-Content -Path $parametersFile -Encoding ascii
try {
    $commandId = & aws ssm send-command --region $Region --instance-ids $instanceId `
        --document-name AWS-RunShellScript --parameters "file://$parametersFile" `
        --timeout-seconds 900 --query Command.CommandId --output text
    if ($LASTEXITCODE -ne 0) { throw 'ssm send-command failed' }
} finally {
    Remove-Item $parametersFile -ErrorAction SilentlyContinue
}

$status = 'InProgress'
while ($status -in 'Pending', 'InProgress', 'Delayed') {
    Start-Sleep -Seconds 10
    $status = & aws ssm get-command-invocation --region $Region --command-id $commandId --instance-id $instanceId --query Status --output text
}
& aws ssm get-command-invocation --region $Region --command-id $commandId --instance-id $instanceId --query '[StandardOutputContent,StandardErrorContent]' --output text
if ($status -ne 'Success') { throw "Remote deploy finished with status $status" }

Invoke-Native aws @('cloudfront', 'create-invalidation', '--distribution-id', $distributionId, '--paths', '/*', '--query', 'Invalidation.Id', '--output', 'text')

Write-Host "`nDeployed: $siteUrl" -ForegroundColor Green
Write-Host 'Scraping is disabled in the cloud: load product data into the database, then POST /api/admin/reindex to build the search index.'
