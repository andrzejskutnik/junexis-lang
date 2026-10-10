<#
.SYNOPSIS
Runs the repeatable Junexis Lang 0.2 compiler surface verification.

.DESCRIPTION
This script is intentionally a "pulse check", not a claim of full production
readiness. It separates the current compiler surface into observable gates:

- Release CLI build
- parser-only grammar examples
- semantic check for main examples
- artifact generation for server, embedded and report targets
- project-first workflow for examples/projects/01-temperature-monitor
- native artifact-gate for server/report using local tool caches
- embedded artifact-gate when PlatformIO is available; on Windows the script
  uses short paths for TEMP/TMP and PLATFORMIO_CORE_DIR to avoid ESP32
  toolchain CreateProcess failures caused by very long include paths

Generated audit artifacts are written under .audit-junexis-02 and removed by
default at the end of a successful or failed run.
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$GateExample = "examples/01-temperature-monitor.jnx",
    [switch]$SkipBuild,
    [switch]$SkipArtifactGate,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$auditRoot = Join-Path $repoRoot ".audit-junexis-02"
$outputRoot = Join-Path $auditRoot "compile"
$projectOutputRoot = Join-Path $auditRoot "projects"
$toolCacheRoot = Join-Path $auditRoot "tool-cache"
$cliProject = Join-Path $repoRoot "compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj"
$cliDll = Join-Path $repoRoot "compiler/JunexisLang.Compiler.CLI/bin/$Configuration/net10.0/JunexisLang.Compiler.CLI.dll"
$gateExamplePath = Join-Path $repoRoot $GateExample
$failures = New-Object System.Collections.Generic.List[string]
$reusedExistingCliBecauseOfLspLock = $false

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message) | Out-Null
    Write-Host "FAIL $Message" -ForegroundColor Red
}

function Test-LspHostBuildLock {
    param([string[]]$OutputLines)

    if ($null -eq $OutputLines -or $OutputLines.Count -eq 0) {
        return $false
    }

    $joined = $OutputLines -join [Environment]::NewLine
    return $joined.Contains("JunexisLang.Compiler.LspHost", [System.StringComparison]::OrdinalIgnoreCase) -and
        $joined.Contains("being used by another process", [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-Checked {
    param(
        [string]$Label,
        [string]$File,
        [string[]]$Command
    )

    & $Command[0] $Command[1..($Command.Count - 1)]
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK   $Label $File" -ForegroundColor Green
    }
    else {
        Add-Failure "$Label $File"
    }
}

function Find-PlatformIO {
    $pathCommand = Get-Command "pio" -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand) {
        return $pathCommand.Source
    }

    $localPio = Join-Path $repoRoot ".m4-platformio/Scripts/pio.exe"
    if (Test-Path -LiteralPath $localPio) {
        return $localPio
    }

    return $null
}

function Invoke-EmbeddedArtifactGate {
    param(
        [string]$Pio,
        [string]$ExamplePath
    )

    $oldTemp = $env:TEMP
    $oldTmp = $env:TMP
    $oldPlatformIoCoreDir = $env:PLATFORMIO_CORE_DIR

    try {
        if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
            $shortTemp = "C:\tmp"
            New-Item -ItemType Directory -Path $shortTemp -Force | Out-Null
            $env:TEMP = $shortTemp
            $env:TMP = $shortTemp
            $env:PLATFORMIO_CORE_DIR = Join-Path $shortTemp "pio-core"
            New-Item -ItemType Directory -Path $env:PLATFORMIO_CORE_DIR -Force | Out-Null

            Write-Host "INFO embedded gate uses short Windows paths: TEMP/TMP=$shortTemp, PLATFORMIO_CORE_DIR=$env:PLATFORMIO_CORE_DIR" -ForegroundColor DarkGray
        }

        Invoke-Checked "artifact-gate:embedded" $ExamplePath @("dotnet", $cliDll, "artifact-gate", $ExamplePath, "--target", "embedded", "--platformio-exe", $Pio)
    }
    finally {
        $env:TEMP = $oldTemp
        $env:TMP = $oldTmp
        $env:PLATFORMIO_CORE_DIR = $oldPlatformIoCoreDir
    }
}

function Remove-AuditRoot {
    param(
        [switch]$BestEffort
    )

    if (-not (Test-Path -LiteralPath $auditRoot)) {
        return
    }

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $auditRoot -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 5) {
                if ($BestEffort) {
                    Write-Host "Warning: could not remove audit directory '$auditRoot'. It is ignored by git and can be deleted later." -ForegroundColor Yellow
                    return
                }

                throw
            }

            Start-Sleep -Seconds $attempt
        }
    }
}

Push-Location $repoRoot
try {
    if (Test-Path -LiteralPath $auditRoot) {
        Remove-AuditRoot
    }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $projectOutputRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $toolCacheRoot -Force | Out-Null

    Write-Step "Build CLI ($Configuration)"
    if (-not $SkipBuild) {
        $buildOutput = @(& dotnet build $cliProject -c $Configuration --no-restore 2>&1)
        $buildOutput | ForEach-Object { Write-Host $_ }

        if ($LASTEXITCODE -ne 0) {
            $lockDetected = Test-LspHostBuildLock -OutputLines $buildOutput
            if ($lockDetected -and (Test-Path -LiteralPath $cliDll)) {
                $reusedExistingCliBecauseOfLspLock = $true
                Write-Host "WARN build blocked by active JunexisLang.Compiler.LspHost; continuing with existing CLI binary: $cliDll" -ForegroundColor Yellow
                Write-Host "Hint: restart the VS Code language server/window if you need a fresh Release rebuild before rerunning this pulse check." -ForegroundColor Yellow
            }
            else {
                Add-Failure "dotnet build $cliProject -c $Configuration"
                Write-Host "Hint: if Release DLLs are locked by JunexisLang.Compiler.LspHost, restart the VS Code language server/window and rerun this script." -ForegroundColor Yellow
                Write-Host "Hint: use -SkipBuild only when you intentionally want to verify the already-built CLI." -ForegroundColor Yellow
            }
        }
    }
    else {
        Write-Host "SKIP build requested"
    }

    if (-not (Test-Path -LiteralPath $cliDll)) {
        throw "CLI assembly not found: $cliDll"
    }

    Write-Step "CLI command surface"
    & dotnet $cliDll --help
    if ($LASTEXITCODE -ne 0) {
        Add-Failure "CLI --help"
    }

    Write-Step "Parse grammar examples"
    $grammarExamples = Get-ChildItem -Path (Join-Path $repoRoot "examples/grammar") -Filter "*.jnx" | Sort-Object Name
    foreach ($example in $grammarExamples) {
        Invoke-Checked "parse" $example.FullName @("dotnet", $cliDll, "parse", $example.FullName)
    }

    Write-Step "Check main examples"
    $mainExamples = Get-ChildItem -Path (Join-Path $repoRoot "examples") -Filter "*.jnx" | Sort-Object Name
    foreach ($example in $mainExamples) {
        Invoke-Checked "check" $example.FullName @("dotnet", $cliDll, "check", $example.FullName)
    }

    Write-Step "Compile artifacts for server, embedded and report"
    $targets = @("server", "embedded", "report")
    foreach ($target in $targets) {
        foreach ($example in $mainExamples) {
            $exampleOut = Join-Path (Join-Path $outputRoot $target) $example.BaseName
            Invoke-Checked "compile:$target" $example.FullName @("dotnet", $cliDll, "compile", $example.FullName, "--target", $target, "--out", $exampleOut)
        }
    }

    Write-Step "Project-first workflow example"
    $projectExample = Join-Path $repoRoot "examples/projects/01-temperature-monitor"
    $projectExampleOut = Join-Path $projectOutputRoot "01-temperature-monitor"
    Invoke-Checked "project:doctor" $projectExample @("dotnet", $cliDll, "doctor", $projectExample)
    Invoke-Checked "project:check" $projectExample @("dotnet", $cliDll, "check", $projectExample)
    Invoke-Checked "project:build" $projectExample @("dotnet", $cliDll, "build", $projectExample, "--out", $projectExampleOut)
    Invoke-Checked "project:clean:dry-run" $projectExample @("dotnet", $cliDll, "clean", $projectExample, "--dry-run")

    if (-not $SkipArtifactGate) {
        if (-not (Test-Path -LiteralPath $gateExamplePath)) {
            throw "Gate example not found: $gateExamplePath"
        }

        Write-Step "Artifact gate for representative server/report example"
        $oldGoCache = $env:GOCACHE
        $oldGoPath = $env:GOPATH
        $oldNpmCache = $env:npm_config_cache
        try {
            $env:GOCACHE = Join-Path $toolCacheRoot "go-build"
            $env:GOPATH = Join-Path $toolCacheRoot "go"
            $env:npm_config_cache = Join-Path $toolCacheRoot "npm"
            New-Item -ItemType Directory -Path $env:GOCACHE -Force | Out-Null
            New-Item -ItemType Directory -Path $env:GOPATH -Force | Out-Null
            New-Item -ItemType Directory -Path $env:npm_config_cache -Force | Out-Null

            Invoke-Checked "artifact-gate:server" $gateExamplePath @("dotnet", $cliDll, "artifact-gate", $gateExamplePath, "--target", "server")
            Invoke-Checked "artifact-gate:report" $gateExamplePath @("dotnet", $cliDll, "artifact-gate", $gateExamplePath, "--target", "report")

            $pio = Find-PlatformIO
            if ($null -ne $pio) {
                Invoke-EmbeddedArtifactGate -Pio $pio -ExamplePath $gateExamplePath
            }
            else {
                Write-Host "SKIP artifact-gate:embedded $gateExamplePath (PlatformIO/pio not found)" -ForegroundColor Yellow
            }
        }
        finally {
            $env:GOCACHE = $oldGoCache
            $env:GOPATH = $oldGoPath
            $env:npm_config_cache = $oldNpmCache
        }
    }
    else {
        Write-Step "Artifact gate"
        Write-Host "SKIP artifact-gate requested"
    }

    Write-Step "Summary"
    if ($failures.Count -eq 0) {
        if ($reusedExistingCliBecauseOfLspLock) {
            Write-Host "Junexis 0.2 pulse check passed using the existing CLI build because Release outputs were locked by an active LSP host." -ForegroundColor Green
            exit 0
        }

        Write-Host "Junexis 0.2 pulse check passed." -ForegroundColor Green
        exit 0
    }

    Write-Host "Junexis 0.2 pulse check failed with $($failures.Count) issue(s):" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "- $failure" -ForegroundColor Red
    }
    exit 1
}
finally {
    Pop-Location

    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $auditRoot)) {
        Remove-AuditRoot -BestEffort
    }
    elseif ($KeepArtifacts) {
        Write-Host ""
        Write-Host "Audit artifacts kept at: $auditRoot" -ForegroundColor Yellow
    }
}
