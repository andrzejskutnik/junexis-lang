<#!
.SYNOPSIS
Validates the synthetic Task I / Task J reconstruction branch.

.DESCRIPTION
This script verifies the intentionally synthetic two-step reconstruction used
on feat/task-i-j-reconstruction:

- exactly two commits ahead of the chosen base ref
- Task I commit keeps ingest-compatible tracing only
- Task J commit restores payloadHash-based startup correlation
- reconstruction report and changelog note are present
- final Go golden test still passes
#>

[CmdletBinding()]
param(
    [string]$BaseRef = "origin/main"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$goGeneratorPath = "compiler/JunexisLang.Compiler.CodeGen/GoCodeGenerator.cs"
$goldenTestPath = "compiler/JunexisLang.Compiler.CodeGen.Tests/GoldenGoCodeGenTests.cs"
$reportPath = "docs/TASK-I-TASK-J-RECONSTRUCTION-2026-06-30.md"
$changelogPath = "CHANGELOG.md"
$testProject = Join-Path $repoRoot "compiler/JunexisLang.Compiler.CodeGen.Tests/JunexisLang.Compiler.CodeGen.Tests.csproj"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Context
    )

    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        throw "$Context is missing expected text: $Needle"
    }
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Context
    )

    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -ge 0) {
        throw "$Context contains forbidden text: $Needle"
    }
}

function Get-GitText {
    param([string[]]$Arguments)

    $result = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed"
    }

    return ($result -join [Environment]::NewLine)
}

Push-Location $repoRoot
try {
    Write-Step "Resolve reconstruction commit chain"
    $baseCommit = Get-GitText @("merge-base", $BaseRef, "HEAD")
    $commitsRaw = Get-GitText @("rev-list", "--reverse", "$baseCommit..HEAD")
    $commits = $commitsRaw.Split([Environment]::NewLine, [System.StringSplitOptions]::RemoveEmptyEntries)

    Assert-True ($commits.Count -eq 2) "Expected exactly 2 reconstruction commits ahead of $BaseRef, got $($commits.Count)."

    $taskICommit = $commits[0]
    $taskJCommit = $commits[1]
    $taskISubject = Get-GitText @("log", "-1", "--format=%s", $taskICommit)
    $taskJSubject = Get-GitText @("log", "-1", "--format=%s", $taskJCommit)

    Assert-Contains $taskISubject "[TASK-I]" "Task I commit subject"
    Assert-Contains $taskJSubject "[TASK-J]" "Task J commit subject"

    Write-Step "Verify Task I ingest-only state"
    $taskIGenerator = Get-GitText @("show", "$taskICommit`:$goGeneratorPath")
    $taskIGolden = Get-GitText @("show", "$taskICommit`:$goldenTestPath")

    Assert-Contains $taskIGenerator "func _emitTrace(kind string, taskId uint32, narrativeId string)" "Task I Go generator"
    Assert-Contains $taskIGenerator '_emitTrace(\"TASK_START\",' "Task I Go generator"
    Assert-NotContains $taskIGenerator "payloadHash" "Task I Go generator"
    Assert-NotContains $taskIGenerator "COMM_SEND" "Task I Go generator"
    Assert-NotContains $taskIGenerator "COMM_RECV" "Task I Go generator"

    Assert-Contains $taskIGolden "func _emitTrace(kind string, taskId uint32, narrativeId string)" "Task I golden test"
    Assert-Contains $taskIGolden 'Assert.Contains("_emitTrace(\"TASK_START\",' "Task I golden test"
    Assert-Contains $taskIGolden 'Assert.DoesNotContain("payloadHash"' "Task I golden test"

    Write-Step "Verify Task J correlated final state"
    $taskJGenerator = Get-GitText @("show", "HEAD:$goGeneratorPath")
    $taskJGolden = Get-GitText @("show", "HEAD:$goldenTestPath")
    $taskJReport = Get-GitText @("show", "HEAD:$reportPath")
    $taskJChangelog = Get-GitText @("show", "HEAD:$changelogPath")

    Assert-Contains $taskJGenerator "func _emitTrace(kind string, taskId uint32, narrativeId string, payloadHash string)" "Task J Go generator"
    Assert-Contains $taskJGenerator '_emitTrace(\"COMM_SEND\",' "Task J Go generator"
    Assert-Contains $taskJGenerator '_emitTrace(\"COMM_RECV\",' "Task J Go generator"
    Assert-Contains $taskJGenerator "payloadHash" "Task J Go generator"

    Assert-Contains $taskJGolden "func _emitTrace(kind string, taskId uint32, narrativeId string, payloadHash string)" "Task J golden test"
    Assert-Contains $taskJGolden 'Assert.Contains("_emitTrace(\"COMM_SEND\",' "Task J golden test"
    Assert-Contains $taskJGolden 'Assert.Contains("_emitTrace(\"COMM_RECV\",' "Task J golden test"

    Assert-Contains $taskJReport "Manual split logiczny" "Reconstruction report"
    Assert-Contains $taskJChangelog "feat/task-i-j-reconstruction" "Changelog reconstruction note"

    Write-Step "Run narrow Go golden validation"
    & dotnet test $testProject -v q --filter Golden_03_SmartHome_EmitsTraceHelperWithNarrativeId
    if ($LASTEXITCODE -ne 0) {
        throw "Narrow Go golden validation failed."
    }

    Write-Step "Summary"
    Write-Host "Task I / Task J reconstruction check passed." -ForegroundColor Green
    Write-Host "Task I commit: $taskICommit" -ForegroundColor Green
    Write-Host "Task J commit: $taskJCommit" -ForegroundColor Green
}
finally {
    Pop-Location
}