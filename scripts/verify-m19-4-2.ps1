[CmdletBinding()]
param(
    [string] $Version = '0.19.0-preview.1',
    [switch] $BuildAll,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repositoryRoot
$solutionPath = Join-Path $repositoryRoot 'compiler/JunexisLang.Compiler.slnx'
$publisherPath = Join-Path $PSScriptRoot 'publish-m19-4-2.ps1'
$compatibilityPath = Join-Path $workspaceRoot 'junexis-sdk/compatibility/platform-variants-0.1.json'
$nugetConfig = Join-Path $repositoryRoot 'nuget.release.config'
$releaseCache = Join-Path $repositoryRoot '.artifacts/m19-4-2/cache'
$env:APPDATA = Join-Path $releaseCache 'appdata'
$env:NUGET_PACKAGES = Join-Path $releaseCache 'packages'
New-Item -ItemType Directory -Path $env:APPDATA, $env:NUGET_PACKAGES -Force | Out-Null

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

foreach ($requiredPath in @($solutionPath, $publisherPath, $compatibilityPath, $nugetConfig)) {
    Assert-Condition (Test-Path -LiteralPath $requiredPath -PathType Leaf) "Brakuje elementu M19.4.2: $requiredPath"
}

$compatibility = Get-Content -LiteralPath $compatibilityPath -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedRids = @('linux-x64', 'osx-arm64', 'win-x64')
$contractRids = @($compatibility.supportedRids | Sort-Object)
Assert-Condition (($contractRids -join '|') -ceq ($expectedRids -join '|')) `
    'SDK nie opisuje zamkniętej macierzy M19.'

if (-not $NoRestore) {
    & dotnet restore $solutionPath --configfile $nugetConfig --ignore-failed-sources --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Odtwarzanie zależności testów zakończyło się kodem $LASTEXITCODE." }
}
& dotnet test $solutionPath --no-restore --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Testy Kompilatora i LSP zakończyły się kodem $LASTEXITCODE." }

$nativeRid = Get-NativeRid
if ($BuildAll) {
    foreach ($rid in $compatibility.supportedRids) {
        $arguments = @{
            RuntimeIdentifier = [string] $rid
            Version = $Version
            AllowCrossBuild = $true
        }
        if ($NoRestore) { $arguments.NoRestore = $true }
        & $publisherPath @arguments
        if ($LASTEXITCODE -ne 0) { throw "Budowanie wariantu $rid zakończyło się kodem $LASTEXITCODE." }
    }
}

foreach ($rid in $compatibility.supportedRids) {
    $receiptPath = Join-Path $repositoryRoot ".artifacts/m19-4-2/$Version/$rid/junexis.language-tools-build-receipt.json"
    Assert-Condition (Test-Path -LiteralPath $receiptPath -PathType Leaf) "Brakuje rachunku budowania dla $rid."
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($receipt.schemaVersion -ceq 'junexis.language-tools-build-receipt/0.1') `
        "Rachunek $rid ma nieznany kontrakt."
    Assert-Condition ($receipt.rid -ceq $rid) "Rachunek opisuje inny RID niż $rid."
    Assert-Condition ((@($receipt.components.id | Sort-Object) -join '|') -ceq 'compiler-cli|lsp') `
        "Rachunek $rid nie obejmuje dokładnie Kompilatora i LSP."
    Assert-Condition ($receipt.artifact.sizeBytes -gt 0) "Artefakt $rid jest pusty."
    Assert-Condition ([string]$receipt.artifact.sha256 -match '^[0-9a-f]{64}$') "Artefakt $rid ma niepoprawny SHA-256."
    $artifactPath = Join-Path (Split-Path -Parent $receiptPath) $receipt.artifact.file
    Assert-Condition (Test-Path -LiteralPath $artifactPath -PathType Leaf) "Brakuje artefaktu wskazanego przez rachunek $rid."
    $actualHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($actualHash -ceq $receipt.artifact.sha256) "SHA-256 artefaktu $rid nie zgadza się z rachunkiem."
    if ($receipt.proof.kind -ceq 'native-smoke') {
        Assert-Condition ($receipt.proof.hostRid -ceq $rid -and @($receipt.proof.checks).Count -eq 2) `
            "Natywny rachunek $rid nie pochodzi z właściwego systemu lub nie obejmuje obu narzędzi."
    }
    elseif ($receipt.proof.kind -ceq 'cross-build-only') {
        Assert-Condition ($receipt.proof.hostRid -cne $rid -and @($receipt.proof.checks).Count -eq 0) `
            "Krzyżowy rachunek $rid nieuczciwie udaje próbę natywną."
    }
    else {
        throw "Wariant $rid ma nieznany rodzaj dowodu $($receipt.proof.kind)."
    }
}

$evidenceRids = @()
foreach ($rid in $contractRids) {
    $evidencePath = Join-Path $repositoryRoot "evidence/m19-4-2/$rid.json"
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { continue }
    $evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($evidence.rid -ceq $rid -and $evidence.proof.hostRid -ceq $rid) `
        "Wersjonowany dowód narzędzi języka $rid opisuje inny system niż wykonawca."
    Assert-Condition ($evidence.proof.kind -ceq 'native-smoke') `
        "Wersjonowany dowód narzędzi języka $rid nie pochodzi z próby natywnej."
    Assert-Condition (@($evidence.proof.checks).Count -eq 2) `
        "Dowód narzędzi języka $rid nie obejmuje Kompilatora i LSP."
    $receiptPath = Join-Path $repositoryRoot ".artifacts/m19-4-2/$Version/$rid/junexis.language-tools-build-receipt.json"
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($evidence.artifact.sha256 -ceq $receipt.artifact.sha256) `
        "Wersjonowany dowód narzędzi języka $rid nie odpowiada bieżącemu artefaktowi."
    $evidenceRids += $rid
}

$global:LASTEXITCODE = 0
Write-Output 'M19.4.2 ZAIMPLEMENTOWANO: Kompilator i LSP mają samodzielne paczki dla całej zamkniętej macierzy.'
$missingEvidence = @($contractRids | Where-Object { $_ -notin $evidenceRids })
if ($missingEvidence.Count -eq 0) {
    Write-Output 'M19.4.2 VERIFIED: Kompilator i LSP mają natywne dowody z całej macierzy.'
}
else {
    Write-Output "Status VERIFIED wymaga jeszcze natywnych rachunków z: $($missingEvidence -join ', ')."
}
