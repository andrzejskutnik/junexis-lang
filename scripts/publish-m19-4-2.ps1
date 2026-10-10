[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [string] $Version = '0.19.0-preview.1',
    [string] $OutputPath = '',
    [switch] $AllowCrossBuild,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot '.artifacts/m19-4-2'
$platforms = @{
    'win-x64' = [ordered]@{ system = 'windows'; architecture = 'x64'; extension = '.zip' }
    'linux-x64' = [ordered]@{ system = 'linux'; architecture = 'x64'; extension = '.tar.gz' }
    'osx-arm64' = [ordered]@{ system = 'macos'; architecture = 'arm64'; extension = '.tar.gz' }
}

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-NativeRid {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    if ($IsWindows -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) { return 'win-x64' }
    if ($IsLinux -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) { return 'linux-x64' }
    if ($IsMacOS -and $architecture -eq [Runtime.InteropServices.Architecture]::Arm64) { return 'osx-arm64' }
    return $null
}

$platform = $platforms[$RuntimeIdentifier]
$nativeRid = Get-NativeRid
$isNativeBuild = $nativeRid -ceq $RuntimeIdentifier
if (-not $isNativeBuild -and -not $AllowCrossBuild) {
    throw "Publikowalny wariant $RuntimeIdentifier musi powstać na natywnym wykonawcy. Bieżący RID: $($nativeRid ?? 'niewspierany')."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $artifactRoot "$Version/$RuntimeIdentifier"
}
$resolvedArtifactRoot = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\', '/')
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath).TrimEnd('\', '/')
Assert-Condition (
    $resolvedOutput.StartsWith($resolvedArtifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
    "Katalog wynikowy musi pozostać wewnątrz $resolvedArtifactRoot."

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
$compilerPath = Join-Path $resolvedOutput 'compiler'
$lspPath = Join-Path $resolvedOutput 'lsp'
New-Item -ItemType Directory -Path $compilerPath, $lspPath -Force | Out-Null

$compilerArguments = @{
    RuntimeIdentifier = $RuntimeIdentifier
    Configuration = 'Release'
    OutputPath = $compilerPath
    Version = $Version
    SelfContained = $true
}
if ($NoRestore) { $compilerArguments.NoRestore = $true }
& (Join-Path $PSScriptRoot 'publish-cli.ps1') @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Publikowanie Kompilatora dla $RuntimeIdentifier nie powiodło się." }

$lspArguments = @{
    RuntimeIdentifier = $RuntimeIdentifier
    Configuration = 'Release'
    OutputPath = $lspPath
    Version = $Version
    SelfContained = $true
}
if ($NoRestore) { $lspArguments.NoRestore = $true }
& (Join-Path $PSScriptRoot 'publish-lsp.ps1') @lspArguments
if ($LASTEXITCODE -ne 0) { throw "Publikowanie LSP dla $RuntimeIdentifier nie powiodło się." }

$compilerManifestPath = Join-Path $compilerPath 'junexis-cli.json'
$lspManifestPath = Join-Path $lspPath 'junexis-lsp.json'
$compilerManifest = Get-Content -LiteralPath $compilerManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$lspManifest = Get-Content -LiteralPath $lspManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($manifest in @($compilerManifest, $lspManifest)) {
    Assert-Condition ($manifest.runtimeIdentifier -ceq $RuntimeIdentifier) 'Manifest opisuje inny RID niż budowany wariant.'
    Assert-Condition ($manifest.selfContained -eq $true) 'Wariant M19.4.2 musi być samodzielny.'
}

$smokeResults = @()
if ($isNativeBuild) {
    $compilerExecutable = Join-Path $compilerPath $compilerManifest.entrypoint
    $lspExecutable = Join-Path $lspPath $lspManifest.entrypoint
    if (-not $IsWindows) {
        & chmod 755 $compilerExecutable $lspExecutable
        if ($LASTEXITCODE -ne 0) { throw 'Nie udało się nadać praw wykonania narzędziom języka.' }
    }

    $compilerOutput = & $compilerExecutable --help 2>&1
    Assert-Condition ($LASTEXITCODE -eq 0) 'Natywna próba Kompilatora zakończyła się błędem.'
    Assert-Condition (($compilerOutput -join "`n").Contains('Junexis', [StringComparison]::Ordinal)) `
        'Kompilator nie zwrócił oczekiwanej tożsamości.'
    $smokeResults += [ordered]@{ component = 'compiler-cli'; command = "$($compilerManifest.entrypoint) --help"; exitCode = 0 }

    $lspIdentityJson = & $lspExecutable --version 2>&1
    Assert-Condition ($LASTEXITCODE -eq 0) 'Natywna próba LSP zakończyła się błędem.'
    $lspIdentity = ($lspIdentityJson -join "`n") | ConvertFrom-Json
    Assert-Condition ($lspIdentity.name -ceq 'JunexisLang.Compiler.LspHost') 'LSP zwrócił obcą tożsamość.'
    Assert-Condition ($lspIdentity.version -ceq $lspManifest.version) 'Tożsamość LSP i jego manifest mają różne wersje.'
    $smokeResults += [ordered]@{ component = 'lsp'; command = "$($lspManifest.entrypoint) --version"; exitCode = 0 }
}

$archiveBaseName = "junexis-language-tools-$Version-$RuntimeIdentifier"
$archivePath = Join-Path $resolvedOutput ($archiveBaseName + $platform.extension)
if ($platform.extension -ceq '.zip') {
    Compress-Archive -Path $compilerPath, $lspPath -DestinationPath $archivePath -CompressionLevel Optimal
}
else {
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    Assert-Condition ($null -ne $tar) 'Pakowanie tar.gz wymaga programu tar.'
    & $tar.Source -czf $archivePath -C $resolvedOutput compiler lsp
    if ($LASTEXITCODE -ne 0) { throw "Pakowanie $RuntimeIdentifier zakończyło się kodem $LASTEXITCODE." }
}

$archive = Get-Item -LiteralPath $archivePath
$receipt = [ordered]@{
    schemaVersion = 'junexis.language-tools-build-receipt/0.1'
    version = $Version
    rid = $RuntimeIdentifier
    system = $platform.system
    architecture = $platform.architecture
    components = @(
        [ordered]@{ id = 'compiler-cli'; entrypoint = $compilerManifest.entrypoint; protocolVersion = $compilerManifest.protocolVersion },
        [ordered]@{ id = 'lsp'; entrypoint = $lspManifest.entrypoint; protocolVersion = $lspManifest.protocolVersion }
    )
    artifact = [ordered]@{
        file = $archive.Name
        sizeBytes = $archive.Length
        sha256 = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    proof = [ordered]@{
        kind = $(if ($isNativeBuild) { 'native-smoke' } else { 'cross-build-only' })
        hostRid = $nativeRid
        checks = $smokeResults
    }
}
$receiptPath = Join-Path $resolvedOutput 'junexis.language-tools-build-receipt.json'
$receiptJson = $receipt | ConvertTo-Json -Depth 8
$receiptJson | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
if ($isNativeBuild) {
    $evidenceRoot = Join-Path $repositoryRoot 'evidence/m19-4-2'
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    $receiptJson | Set-Content -LiteralPath (Join-Path $evidenceRoot "$RuntimeIdentifier.json") -Encoding utf8NoBOM
}

Write-Output "M19.4.2 UTWORZONO: $archivePath"
Write-Output "M19.4.2 DOWÓD: $($receipt.proof.kind) dla $RuntimeIdentifier"
