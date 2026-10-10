[CmdletBinding()]
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$platformRoot = Split-Path $langRoot -Parent
$runtimeRoot = Join-Path $platformRoot 'junexis-runtime'
$providersRoot = Join-Path $platformRoot 'junexis-storage-providers'
$gateRoot = Join-Path $langRoot '.m13-11-gate'

function Invoke-Checked([string] $Name, [scriptblock] $Action) {
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $gateRoot) {
    $resolved = (Resolve-Path -LiteralPath $gateRoot).Path
    if (-not $resolved.StartsWith($langRoot + '\.m13-11-gate', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected gate path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

try {
    if (-not $SkipBuild) {
        Invoke-Checked 'Storage Runtime Host build' {
            dotnet build (Join-Path $runtimeRoot 'Junexis.Storage.Runtime.Host/Junexis.Storage.Runtime.Host.csproj') --no-restore
        }
        Invoke-Checked 'memory provider build' {
            dotnet build (Join-Path $providersRoot 'Junexis.Storage.Provider.Memory/Junexis.Storage.Provider.Memory.csproj') --no-restore
        }
        Invoke-Checked 'compiler build' {
            dotnet build (Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj') --no-restore
        }
    }

    $project = Join-Path $langRoot 'examples/projects/13-imported-storage-query'
    $cli = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
    Invoke-Checked 'Junexis modular project build' {
        dotnet run --project $cli --no-build -- build $project --out $gateRoot
    }

    $serverRoot = Join-Path $gateRoot 'server'
    Copy-Item -LiteralPath (Join-Path $langRoot 'tests/m13-11/imported_task_query_e2e_test.go') -Destination $serverRoot
    $env:JUNEXIS_M13_HOST = Join-Path $runtimeRoot 'Junexis.Storage.Runtime.Host/bin/Debug/net10.0/Junexis.Storage.Runtime.Host.dll'
    $env:JUNEXIS_M13_PROVIDER = Join-Path $providersRoot 'Junexis.Storage.Provider.Memory/bin/Debug/net10.0/Junexis.Storage.Provider.Memory.dll'
    $env:GOCACHE = New-Item -ItemType Directory -Force -Path (Join-Path $gateRoot 'go-cache') | Select-Object -ExpandProperty FullName
    $env:GOMODCACHE = New-Item -ItemType Directory -Force -Path (Join-Path $gateRoot 'go-mod-cache') | Select-Object -ExpandProperty FullName
    Push-Location $serverRoot
    try {
        Invoke-Checked 'imported task query E2E' { go test -run TestJunexisTaskExecutesImportedNamedQuery -v . }
    }
    finally {
        Pop-Location
    }

    Write-Host 'M13.11 VERIFIED: imported storage module -> .jnx task -> named query -> Runtime Host -> memory provider.'
}
finally {
    if (Test-Path -LiteralPath $gateRoot) {
        $resolved = (Resolve-Path -LiteralPath $gateRoot).Path
        if ($resolved.StartsWith($langRoot + '\.m13-11-gate', [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
