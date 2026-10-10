[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$langRoot = (Resolve-Path (Join-Path $projectRoot '../../../..')).Path
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workRoot = Join-Path $tempRoot "junexis-controlled-recovery-$([Guid]::NewGuid().ToString('N'))"
$stdoutPath = Join-Path $workRoot 'stdout.log'
$stderrPath = Join-Path $workRoot 'stderr.log'
$process = $null

function Get-RecoveryState([string]$PanelUrl) {
    return Invoke-RestMethod -Uri ($PanelUrl + 'api/home')
}

function Invoke-RecoveryPost([string]$PanelUrl, [string]$Path) {
    return Invoke-RestMethod -Method Post -ContentType 'application/json' -Body '{}' -Uri ($PanelUrl + $Path)
}

function Wait-RecoveryState([string]$PanelUrl, [scriptblock]$Predicate, [int]$TimeoutSeconds = 24) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $state = Get-RecoveryState $PanelUrl
        if (& $Predicate $state) { return $state }
    }
    throw 'Timed out waiting for the expected controlled-recovery state.'
}

New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $cliProject, '--no-build', '--',
        'run', $projectRoot, '--duration', '52'
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
    if (-not $panelUrl) { throw 'Controlled-recovery panel did not publish its local URL.' }

    $page = Invoke-WebRequest -UseBasicParsing -Uri $panelUrl
    foreach ($phrase in @('Kontrolowany powrót', 'Świeże pomiary', 'Zatwierdź recovery', 'Oś recovery')) {
        if (-not $page.Content.Contains($phrase)) { throw "Controlled-recovery panel is missing phrase: $phrase" }
    }

    $baseline = Wait-RecoveryState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'normal' -and $state.recovery_mode -eq 'automatic' -and
            $state.rooms.Count -eq 3 -and $salon.air_quality -eq 1250 -and $salon.actual_enabled
    }

    $null = Invoke-RecoveryPost $panelUrl 'api/source/stop'
    $limited = Wait-RecoveryState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'limited' -and $salon.actual_enabled -and
            $salon.status -eq 'limited-source-silent'
    }
    $null = Invoke-RecoveryPost $panelUrl 'api/source/resume'
    $restoredAutomatically = Wait-RecoveryState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'normal' -and $state.fresh_evidence -eq 2 -and
            $salon.status -eq 'applied' -and $salon.actual_enabled
    }
    foreach ($title in @('Wznowiono źródło salonu', 'Automatyczne recovery zakończone')) {
        if ($title -notin @($restoredAutomatically.timeline.title)) { throw "Automatic timeline is missing: $title" }
    }
    if (@($restoredAutomatically.timeline.title | Where-Object { $_ -eq 'Przyjęto świeży pomiar salonu' }).Count -lt 2) {
        throw 'Automatic recovery did not preserve two fresh-reading evidence events.'
    }

    $null = Invoke-RecoveryPost $panelUrl 'api/reset'
    $manualMode = Invoke-RecoveryPost $panelUrl 'api/recovery/manual'
    if ($manualMode.recovery_mode -ne 'manual-approval') { throw 'Manual recovery mode was not applied.' }
    $null = Invoke-RecoveryPost $panelUrl 'api/source/stop'
    $null = Wait-RecoveryState $panelUrl { param($state) $state.recovery_phase -eq 'limited' }
    $null = Invoke-RecoveryPost $panelUrl 'api/source/resume'
    $awaiting = Wait-RecoveryState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'awaiting-approval' -and $state.fresh_evidence -eq 2 -and
            -not $state.approved -and $salon.actual_enabled -and $salon.status -eq 'held-recovery-validation'
    }
    Start-Sleep -Milliseconds 1500
    if ((Get-RecoveryState $panelUrl).recovery_phase -ne 'awaiting-approval') {
        throw 'Manual recovery bypassed human approval.'
    }
    $approved = Invoke-RecoveryPost $panelUrl 'api/recovery/approve'
    if ($approved.recovery_phase -ne 'approved' -or -not $approved.approved) {
        throw 'Human approval did not create an explicit approved phase.'
    }
    $restoredManually = Wait-RecoveryState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.recovery_phase -eq 'normal' -and $state.approved -and
            $salon.status -eq 'applied' -and $salon.actual_enabled
    }
    foreach ($title in @('Warunki danych są spełnione', 'Człowiek zatwierdził recovery', 'Ręczne recovery zakończone')) {
        if ($title -notin @($restoredManually.timeline.title)) { throw "Manual timeline is missing: $title" }
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "junexis run exited with code $($process.ExitCode)." }
    Write-Output "PASS: Dom 8 proved automatic and human-approved recovery at $panelUrl"
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
