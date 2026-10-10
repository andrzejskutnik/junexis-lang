[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$platformRoot = (Resolve-Path (Join-Path $langRoot '..')).Path
$runtimeRoot = Join-Path $platformRoot 'junexis-runtime'
$providersRoot = Join-Path $platformRoot 'junexis-storage-providers'

if (-not (Test-Path -LiteralPath $runtimeRoot) -or -not (Test-Path -LiteralPath $providersRoot)) {
    throw 'M14.6 requires sibling junexis-runtime and junexis-storage-providers repositories.'
}

& dotnet test (Join-Path $runtimeRoot 'Junexis.Storage.Runtime.Tests/Junexis.Storage.Runtime.Tests.csproj') --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Storage Runtime tests failed with exit code $LASTEXITCODE." }

& (Join-Path $runtimeRoot 'pack-storage-sdk.ps1')
if ($LASTEXITCODE -ne 0) { throw "Storage Runtime SDK packaging failed with exit code $LASTEXITCODE." }

& (Join-Path $providersRoot 'run-provider-gate.ps1') -RuntimePackageSource (Join-Path $runtimeRoot 'artifacts/storage-sdk')
if ($LASTEXITCODE -ne 0) { throw "Storage provider gate failed with exit code $LASTEXITCODE." }

Write-Host 'M14.6 VERIFIED: SQLite persists proven Mutation Plan deltas with parameterized SQL and safe fallback.'
