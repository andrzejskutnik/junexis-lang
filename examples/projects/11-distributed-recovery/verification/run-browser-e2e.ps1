param(
    [string]$BrowserExecutable = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot "out"
$serverRoot = Join-Path $outputRoot "server"
$reportRoot = Join-Path $outputRoot "report"
$ownedRunRoot = Join-Path $projectRoot ".browser-run"
$serverExecutable = Join-Path $ownedRunRoot "golden-home-server.exe"

function Invoke-Checked([scriptblock]$Command, [string]$Description) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description zakończyło się kodem $LASTEXITCODE."
    }
}

function Find-Browser {
    if ($BrowserExecutable -ne "") {
        return (Resolve-Path -LiteralPath $BrowserExecutable).Path
    }
    foreach ($candidate in @(
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }
    throw "Nie znaleziono Chrome ani Edge. Podaj -BrowserExecutable."
}

try {
    $browser = Find-Browser
    Write-Host "[1/5] Sprawdzanie projektu"
    Invoke-Checked { junexis check $projectRoot } "junexis check"

    Write-Host "[2/5] Budowanie Domu 11"
    Invoke-Checked { junexis build $projectRoot } "junexis build"

    Write-Host "[3/5] Budowanie serwera Go"
    New-Item -ItemType Directory -Path $ownedRunRoot -Force | Out-Null
    Push-Location $serverRoot
    try {
        Invoke-Checked { go mod tidy } "go mod tidy"
        Invoke-Checked { go build -trimpath -o $serverExecutable . } "go build"
    } finally {
        Pop-Location
    }

    Write-Host "[4/5] Budowanie aplikacji przeglądarkowej"
    Push-Location $reportRoot
    try {
        Invoke-Checked { npm install --ignore-scripts } "npm install"
        Invoke-Checked { npm run app:build } "npm run app:build"
    } finally {
        Pop-Location
    }

    Write-Host "[5/5] Chrome: connected, online, reconnecting i rejoined"
    Invoke-Checked {
        node (Join-Path $PSScriptRoot "verify-browser-e2e.mjs") `
            $serverExecutable $reportRoot $browser
    } "Golden Home Report E2E"
} finally {
    if (Test-Path -LiteralPath $ownedRunRoot) {
        $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path
        $resolvedRun = (Resolve-Path -LiteralPath $ownedRunRoot).Path
        if (-not $resolvedRun.Equals((Join-Path $resolvedProject ".browser-run"), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Odmowa usunięcia nieoczekiwanego katalogu: $resolvedRun"
        }
        Remove-Item -LiteralPath $resolvedRun -Recurse -Force
    }
}
