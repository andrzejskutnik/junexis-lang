[CmdletBinding()]
param(
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$langRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

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
}

Invoke-Checked 'compiler semantic tests' {
    dotnet test (Join-Path $langRoot 'compiler/JunexisLang.Compiler.Tests/JunexisLang.Compiler.Tests.csproj') --no-build --no-restore --verbosity minimal
}
Invoke-Checked 'workspace LSP tests' {
    dotnet test (Join-Path $langRoot 'compiler/JunexisLang.Compiler.LspHost.Tests/JunexisLang.Compiler.LspHost.Tests.csproj') --no-build --no-restore --verbosity minimal
}

Write-Host 'M14.2 VERIFIED: .jnx mutation syntax -> AST -> Structural IR -> provider-neutral Mutation Plan.'

