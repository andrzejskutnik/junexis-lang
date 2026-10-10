[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$langRoot = (Resolve-Path (Join-Path $projectRoot '../../../..')).Path
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workRoot = Join-Path $tempRoot "junexis-recovery-policy-$([Guid]::NewGuid().ToString('N'))"
$stdoutPath = Join-Path $workRoot 'stdout.log'
$stderrPath = Join-Path $workRoot 'stderr.log'
$process = $null

function Read-Policy([string]$PanelUrl) { Invoke-RestMethod -Uri ($PanelUrl + 'api/home') }
function Invoke-PolicyPost([string]$PanelUrl, [string]$Path, [hashtable]$Body = @{}) {
    Invoke-RestMethod -Method Post -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Compress) -Uri ($PanelUrl + $Path)
}
function Wait-Policy([string]$PanelUrl, [scriptblock]$Predicate, [int]$TimeoutSeconds = 25) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $state = Read-Policy $PanelUrl
        if (& $Predicate $state) { return $state }
    }
    throw 'Timed out waiting for the expected recovery-policy state.'
}

New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $cliProject, '--no-build', '--',
        'run', $projectRoot, '--duration', '68'
    ) -WorkingDirectory $langRoot -RedirectStandardOutput $stdoutPath `
      -RedirectStandardError $stderrPath -WindowStyle Hidden -PassThru

    $panelUrl = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    while ([DateTime]::UtcNow -lt $deadline -and -not $panelUrl) {
        Start-Sleep -Milliseconds 200
        $logs = @()
        if (Test-Path -LiteralPath $stdoutPath) { $logs += Get-Content -LiteralPath $stdoutPath -Raw }
        if (Test-Path -LiteralPath $stderrPath) { $logs += Get-Content -LiteralPath $stderrPath -Raw }
        $match = [regex]::Match(($logs -join "`n"), 'PANEL (http://127\.0\.0\.1:\d+/)')
        if ($match.Success) { $panelUrl = $match.Groups[1].Value }
        if ($process.HasExited -and -not $panelUrl) { break }
    }
    if (-not $panelUrl) { throw 'Recovery-policy panel did not publish its local URL.' }

    $page = Invoke-WebRequest -UseBasicParsing -Uri $panelUrl
    foreach ($phrase in @('Ty definiujesz warunki powrotu', 'Efektywna konfiguracja', 'Odrzuć recovery', 'Rollback do profilu bazowego')) {
        if (-not $page.Content.Contains($phrase)) { throw "Recovery-policy panel is missing phrase: $phrase" }
    }

    $null = Wait-Policy $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'normal' -and $state.rooms.Count -eq 3 -and $salon.actual_enabled
    }

    $automatic = Invoke-PolicyPost $panelUrl 'api/policy' @{
        recovery_mode = 'automatic'; required_evidence = 2
        stabilization_delay_ms = 750; evidence_limit = 8
    }
    if ($automatic.policy_revision -ne 1 -or $automatic.effective_config.evidence_limit -ne 8) {
        throw 'Automatic expert policy was not applied as one revision.'
    }
    $null = Invoke-PolicyPost $panelUrl 'api/source/stop'
    $null = Wait-Policy $panelUrl { param($state) $state.recovery_phase -eq 'limited' }
    $null = Invoke-PolicyPost $panelUrl 'api/source/resume'
    $automaticDone = Wait-Policy $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'normal' -and $state.fresh_evidence -eq 2 -and
            $salon.execution_status -eq 'applied' -and $salon.actual_enabled
    }
    foreach ($title in @('Rozpoczęto czas stabilizacji', 'Czas stabilizacji upłynął', 'Automatyczne recovery zakończone')) {
        if ($title -notin @($automaticDone.timeline.title)) { throw "Automatic policy timeline is missing: $title" }
    }
    if ($automaticDone.timeline.Count -gt 8) { throw 'Evidence retention exceeded the configured limit of 8.' }

    $rollback = Invoke-PolicyPost $panelUrl 'api/rollback'
    if ($rollback.recovery_phase -ne 'normal' -or
        $rollback.effective_config.recovery_mode -ne 'manual-approval' -or
        $rollback.effective_config.required_evidence -ne 2 -or
        $rollback.effective_config.stabilization_delay_ms -ne 2000 -or
        $rollback.effective_config.evidence_limit -ne 18) {
        throw 'Rollback did not restore the documented baseline policy.'
    }
    if ($rollback.timeline.Count -ne 1 -or $rollback.timeline[0].title -ne 'Wykonano rollback polityki') {
        throw 'Rollback did not leave a single explicit baseline event.'
    }

    $manual = Invoke-PolicyPost $panelUrl 'api/policy' @{
        recovery_mode = 'manual-approval'; required_evidence = 2
        stabilization_delay_ms = 2000; evidence_limit = 12
    }
    $null = Invoke-PolicyPost $panelUrl 'api/source/stop'
    $null = Wait-Policy $panelUrl { param($state) $state.recovery_phase -eq 'limited' }
    $null = Invoke-PolicyPost $panelUrl 'api/source/resume'
    $awaiting = Wait-Policy $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'awaiting-approval' -and $state.fresh_evidence -eq 2 -and
            $salon.execution_status -eq 'held-by-recovery-policy'
    }
    $rejected = Invoke-PolicyPost $panelUrl 'api/recovery/reject'
    if ($rejected.recovery_phase -ne 'rejected') { throw 'Rejected recovery did not stay explicitly rejected.' }
    Start-Sleep -Milliseconds 1500
    if ((Read-Policy $panelUrl).recovery_phase -ne 'rejected') { throw 'Rejected recovery resumed without a new decision.' }

    $retried = Invoke-PolicyPost $panelUrl 'api/recovery/retry'
    if ($retried.recovery_phase -ne 'validating' -or $retried.fresh_evidence -ne 0) {
        throw 'Retry did not invalidate the previously rejected evidence counter.'
    }
    $null = Wait-Policy $panelUrl { param($state) $state.recovery_phase -eq 'awaiting-approval' -and $state.fresh_evidence -eq 2 }
    $approved = Invoke-PolicyPost $panelUrl 'api/recovery/approve'
    if ($approved.recovery_phase -ne 'approved' -or -not $approved.approved) {
        throw 'Approval did not create an explicit approved phase.'
    }
    $manualDone = Wait-Policy $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'normal' -and $state.approved -and
            $salon.execution_status -eq 'applied'
    }
    foreach ($title in @('Człowiek odrzucił recovery', 'Rozpoczęto ponowną walidację', 'Człowiek zatwierdził recovery', 'Ręczne recovery zakończone')) {
        if ($title -notin @($manualDone.timeline.title)) { throw "Manual policy timeline is missing: $title" }
    }

    $finalRollback = Invoke-PolicyPost $panelUrl 'api/rollback'
    Start-Sleep -Milliseconds 2500
    $stableBaseline = Read-Policy $panelUrl
    if ($stableBaseline.recovery_phase -ne 'normal' -or $stableBaseline.policy_revision -ne $finalRollback.policy_revision) {
        throw 'An obsolete recovery operation changed the state after rollback.'
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "junexis run exited with code $($process.ExitCode)." }
    Write-Output "PASS: Dom 9 proved versioned policy, stabilization, rejection, retry, retention and rollback at $panelUrl"
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    if (Test-Path -LiteralPath $workRoot) {
        $resolvedWork = (Resolve-Path -LiteralPath $workRoot).Path
        if (-not $resolvedWork.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing unexpected temporary path: $resolvedWork"
        }
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}

