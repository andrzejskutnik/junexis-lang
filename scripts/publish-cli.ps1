[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$OutputPath = '',
    [string]$Version = '',
    [switch]$SelfContained,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$nugetConfig = Join-Path $repoRoot 'nuget.release.config'
$releaseCache = Join-Path $repoRoot '.artifacts/m19-4-2/cache'
$env:APPDATA = Join-Path $releaseCache 'appdata'
$env:NUGET_PACKAGES = Join-Path $releaseCache 'packages'
New-Item -ItemType Directory -Path $env:APPDATA, $env:NUGET_PACKAGES -Force | Out-Null

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot "compiler/JunexisLang.Compiler.CLI/bin/$Configuration/publish-$RuntimeIdentifier-r2r"
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$selfContainedValue = $SelfContained.IsPresent.ToString().ToLowerInvariant()
$executableName = if ($RuntimeIdentifier.StartsWith('win-')) {
    'JunexisLang.Compiler.CLI.exe'
} else {
    'JunexisLang.Compiler.CLI'
}

$versionArguments = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $versionArguments += "-p:Version=$Version"
}

$restoreArguments = @('restore', $project, '--runtime', $RuntimeIdentifier, '-p:PublishReadyToRun=true', '--configfile', $nugetConfig, '--ignore-failed-sources', '--nologo', '-v', 'minimal')
if (-not $NoRestore) {
    & dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw "Odtwarzanie zależności CLI dla $RuntimeIdentifier zakończyło się kodem $LASTEXITCODE." }
}

$buildArguments = @('build', $project, '-c', $Configuration, '--no-restore') + $versionArguments
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "Budowanie tożsamości CLI Junexis zakończyło się kodem $LASTEXITCODE."
}

$manifestArguments = @(
    'run', '--project', $project,
    '-c', $Configuration,
    '--no-build'
) + $versionArguments + @(
    '--', '--distribution-manifest',
    $RuntimeIdentifier, $executableName, $selfContainedValue
)
$manifestJson = & dotnet @manifestArguments
if ($LASTEXITCODE -ne 0) {
    throw "Odczyt manifestu dystrybucji CLI Junexis zakończył się kodem $LASTEXITCODE."
}
$manifest = $manifestJson | ConvertFrom-Json
if (
    $manifest.schemaVersion -ne 1 -or
    $manifest.name -ne 'JunexisLang.Compiler.CLI' -or
    $manifest.protocolVersion -ne 2 -or
    $manifest.runtimeIdentifier -ne $RuntimeIdentifier
) {
    throw "CLI Junexis zwróciło niepoprawny manifest dystrybucji: $manifestJson"
}

$arguments = @(
    'publish',
    $project,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '--self-contained', $selfContainedValue,
    '-p:PublishReadyToRun=true',
    '-p:PublishReadyToRunComposite=true',
    '-p:PublishSingleFile=false',
    '--no-restore',
    '-o', $resolvedOutput
)
if ($versionArguments.Count -gt 0) {
    $arguments += $versionArguments
}

Write-Host "Publikowanie CLI Junexis ReadyToRun ($RuntimeIdentifier, samodzielne: $selfContainedValue)"
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Publikowanie CLI Junexis zakończyło się kodem $LASTEXITCODE."
}

$executable = Join-Path $resolvedOutput $executableName
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Nie znaleziono opublikowanego programu CLI Junexis: $executable"
}

$manifestPath = Join-Path $resolvedOutput 'junexis-cli.json'
$manifestJson | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Output $executable
Write-Output $manifestPath
