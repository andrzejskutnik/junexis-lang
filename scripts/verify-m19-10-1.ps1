[CmdletBinding()]
param([switch] $NoRestore)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-Condition([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repositoryRoot
$compatibilityPath = Join-Path $workspaceRoot 'junexis-sdk/compatibility/platform-variants-0.1.json'
$lockSourcePath = Join-Path $repositoryRoot 'compiler/JunexisLang.Compiler.CLI/Project/PackagesLockFile.cs'
$selectorPath = Join-Path $repositoryRoot 'compiler/JunexisLang.Compiler.CLI/Project/PlatformVariantSelector.cs'
$testProject = Join-Path $repositoryRoot 'compiler/JunexisLang.Compiler.CLI.Tests/JunexisLang.Compiler.CLI.Tests.csproj'
foreach ($path in @($compatibilityPath, $lockSourcePath, $selectorPath, $testProject)) {
    Assert-Condition (Test-Path -LiteralPath $path -PathType Leaf) "Brakuje elementu M19.10.1: $path"
}

$compatibility = Get-Content -LiteralPath $compatibilityPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Condition (($compatibility.selection.logicalIdentityFields -join '|') -ceq 'component|version') `
    'Kontrakt SDK zmienił logiczną tożsamość zależności.'
Assert-Condition ($compatibility.selection.artifactSelector -ceq 'rid' -and
    $compatibility.selection.ridInProjectLock -eq $false -and
    $compatibility.selection.fallbackToAnotherRid -eq $false) `
    'Kontrakt SDK nie zachowuje przenośnego wyboru wariantu.'

$lockSource = Get-Content -LiteralPath $lockSourcePath -Raw -Encoding UTF8
Assert-Condition (-not $lockSource.Contains('RuntimeIdentifier', [StringComparison]::Ordinal) -and
    -not $lockSource.Contains('string Rid', [StringComparison]::Ordinal)) `
    'Plik blokady pakietów został związany z RID-em.'

$arguments = @('test', $testProject, '--filter', 'FullyQualifiedName~PlatformVariantSelectorTests', '-v', 'minimal')
if ($NoRestore) { $arguments += '--no-restore' }
& dotnet @arguments
Assert-Condition ($LASTEXITCODE -eq 0) 'Próby wyboru wariantu M19.10.1 nie przeszły.'
Write-Output 'M19.10.1 ZALICZONO: logiczna blokada pozostaje przenośna, a RID wybiera wyłącznie fizyczny artefakt.'
