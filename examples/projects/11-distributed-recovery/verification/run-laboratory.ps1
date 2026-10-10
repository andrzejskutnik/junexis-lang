param(
    [string]$FirstBootId = "boot-alpha",
    [string]$SecondBootId = "boot-beta",
    [int]$ExpectedSecondGeneration = 1,
    [string]$ThirdBootId = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$verificationRoot = $PSScriptRoot
$outputRoot = Join-Path $projectRoot "out"
$serverRoot = Join-Path $outputRoot "server"
$reportRoot = Join-Path $outputRoot "report"
$ownedRunRoot = Join-Path $projectRoot ".laboratory-run"
$serverExecutable = Join-Path $ownedRunRoot "three-environment-home-server.exe"

function Invoke-Checked([scriptblock]$Command, [string]$Description) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description zakończyło się kodem $LASTEXITCODE."
    }
}

try {
    Write-Host "[1/4] Sprawdzanie projektu"
    Invoke-Checked { junexis check $projectRoot } "junexis check"

    Write-Host "[2/4] Budowanie trzech środowisk"
    Invoke-Checked { junexis build $projectRoot } "junexis build"

    Write-Host "[3/4] Przygotowanie procesów serwera i raportu"
    New-Item -ItemType Directory -Path $ownedRunRoot -Force | Out-Null
    Push-Location $serverRoot
    try {
        Invoke-Checked { go mod tidy } "go mod tidy"
        Invoke-Checked { go build -trimpath -o $serverExecutable . } "go build"
    } finally {
        Pop-Location
    }
    Push-Location $reportRoot
    try {
        Invoke-Checked { npm install --ignore-scripts } "npm install"
        Invoke-Checked { npm run app:build } "npm run app:build"
    } finally {
        Pop-Location
    }

    Write-Host "[4/4] Normalny przepływ, przerwanie granicy i ponowne dołączenie"
    & (Join-Path $verificationRoot "verify-distributed-recovery.ps1") `
        -ServerExecutable $serverExecutable `
        -ReportDirectory $reportRoot `
        -FirstBootId $FirstBootId `
        -SecondBootId $SecondBootId `
        -ExpectedSecondGeneration $ExpectedSecondGeneration `
        -ThirdBootId $ThirdBootId
} finally {
    if (Test-Path -LiteralPath $ownedRunRoot) {
        $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path
        $resolvedRun = (Resolve-Path -LiteralPath $ownedRunRoot).Path
        if (-not $resolvedRun.Equals((Join-Path $resolvedProject ".laboratory-run"), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Odmowa usunięcia nieoczekiwanego katalogu: $resolvedRun"
        }
        Remove-Item -LiteralPath $resolvedRun -Recurse -Force
    }
}
