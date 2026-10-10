[CmdletBinding()]
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$platformRoot = Split-Path $langRoot -Parent
$runtimeRoot = Join-Path $platformRoot 'junexis-runtime'
$providersRoot = Join-Path $platformRoot 'junexis-storage-providers'

function Invoke-Checked([string] $Name, [scriptblock] $Action) {
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (-not $SkipBuild) {
    Invoke-Checked 'compiler solution build' {
        dotnet build (Join-Path $langRoot 'compiler/JunexisLang.Compiler.slnx') --no-restore --verbosity minimal
    }
    Invoke-Checked 'Storage Runtime Host build' {
        dotnet build (Join-Path $runtimeRoot 'Junexis.Storage.Runtime.Host/Junexis.Storage.Runtime.Host.csproj') --no-restore --verbosity minimal
    }
    Invoke-Checked 'memory provider build' {
        dotnet build (Join-Path $providersRoot 'Junexis.Storage.Provider.Memory/Junexis.Storage.Provider.Memory.csproj') --no-restore --verbosity minimal
    }
}

Invoke-Checked 'compiler semantic tests' {
    dotnet test (Join-Path $langRoot 'compiler/JunexisLang.Compiler.Tests/JunexisLang.Compiler.Tests.csproj') --no-build --no-restore --verbosity minimal
}
Invoke-Checked 'CLI project tests' {
    dotnet test (Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI.Tests/JunexisLang.Compiler.CLI.Tests.csproj') --no-build --no-restore --verbosity minimal
}
Invoke-Checked 'workspace LSP tests' {
    dotnet test (Join-Path $langRoot 'compiler/JunexisLang.Compiler.LspHost.Tests/JunexisLang.Compiler.LspHost.Tests.csproj') --no-build --no-restore --verbosity minimal
}

Invoke-Checked 'M13.10 task query gate' {
    & (Join-Path $langRoot 'scripts/verify-m13-10.ps1') -SkipBuild
}
Invoke-Checked 'M13.11 imported query gate' {
    & (Join-Path $langRoot 'scripts/verify-m13-11.ps1') -SkipBuild
}
Invoke-Checked 'M13.12 aliased query gate' {
    & (Join-Path $langRoot 'scripts/verify-m13-12.ps1') -SkipBuild
}

Write-Host 'M13.13 VERIFIED: storage query completion + hover + definition + Outline + M13 task/import/alias E2E.'
