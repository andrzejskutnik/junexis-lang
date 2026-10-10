[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$langRoot = (Resolve-Path (Join-Path $projectRoot '../../../..')).Path
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workRoot = Join-Path $tempRoot "junexis-first-silence-$([Guid]::NewGuid().ToString('N'))"
$stdoutPath = Join-Path $workRoot 'stdout.log'
$stderrPath = Join-Path $workRoot 'stderr.log'
$process = $null

function Get-FailureState([string]$PanelUrl) {
    return Invoke-RestMethod -Uri ($PanelUrl + 'api/home')
}

function Wait-FailureState([string]$PanelUrl, [scriptblock]$Predicate, [int]$TimeoutSeconds = 18) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $state = Get-FailureState $PanelUrl
        if (& $Predicate $state) { return $state }
    }
    throw 'Timed out waiting for the expected first-silence state.'
}

New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $cliProject, '--no-build', '--',
        'run', $projectRoot, '--duration', '48'
    ) -WorkingDirectory $langRoot -RedirectStandardOutput $stdoutPath `
      -RedirectStandardError $stderrPath -WindowStyle Hidden -PassThru

    $panelUrl = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $deadline -and -not $panelUrl) {
        Start-Sleep -Milliseconds 200
        $logs = @()
        if (Test-Path -LiteralPath $stdoutPath) { $logs += Get-Content -LiteralPath $stdoutPath -Raw }
        if (Test-Path -LiteralPath $stderrPath) { $logs += Get-Content -LiteralPath $stderrPath -Raw }
        $match = [regex]::Match(($logs -join "`n"), 'PANEL (http://127\.0\.0\.1:\d+/)')
        if ($match.Success) { $panelUrl = $match.Groups[1].Value }
        if ($process.HasExited -and -not $panelUrl) { break }
    }
    if (-not $panelUrl) { throw 'First-silence panel did not publish its local URL.' }

    $page = Invoke-WebRequest -UseBasicParsing -Uri $panelUrl
    foreach ($phrase in @('Pierwsza cisza', 'Zatrzymaj źródło salonu', 'Czas od ostatniego pomiaru salonu', 'Oś przyczyn')) {
        if (-not $page.Content.Contains($phrase)) { throw "First-silence panel is missing phrase: $phrase" }
    }

    $normal = Wait-FailureState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.mode -eq 'normal' -and $state.rooms.Count -eq 3 -and $salon.air_quality -eq 1250 -and $salon.actual_enabled
    }
    $lastSalonAt = $normal.last_salon_at

    $waiting = Invoke-RestMethod -Method Post -ContentType 'application/json' -Body '{}' -Uri ($panelUrl + 'api/source/stop')
    if ($waiting.source_enabled -or $waiting.mode -ne 'waiting') {
        throw 'Stopping the source did not begin the explicit waiting window.'
    }

    Start-Sleep -Milliseconds 1000
    $beforeTimeout = Get-FailureState $panelUrl
    if ($beforeTimeout.mode -eq 'limited' -or $beforeTimeout.silence_age_ms -gt $beforeTimeout.silence_timeout_ms) {
        throw 'System declared silence before the documented timeout.'
    }

    $limited = Wait-FailureState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.mode -eq 'limited' -and $state.silence_age_ms -gt $state.silence_timeout_ms -and
            $salon.source_status -eq 'silent' -and $salon.status -eq 'limited-source-silent'
    } 22
    $limitedSalon = @($limited.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
    if (-not $limitedSalon.actual_enabled) {
        throw 'Limited mode did not preserve the last confirmed salon ventilation state.'
    }
    if ($limited.last_salon_at -ne $lastSalonAt) {
        throw 'The stopped source unexpectedly published a replacement salon reading.'
    }
    foreach ($title in @('Użytkownik zatrzymał źródło salonu', 'Monitor wykrył ciszę salonu')) {
        if ($title -notin @($limited.timeline.title)) { throw "Timeline is missing: $title" }
    }

    $resuming = Invoke-RestMethod -Method Post -ContentType 'application/json' -Body '{}' -Uri ($panelUrl + 'api/source/resume')
    if (-not $resuming.source_enabled -or $resuming.mode -ne 'resuming') {
        throw 'Resume did not wait for fresh evidence.'
    }
    $restored = Wait-FailureState $panelUrl {
        param($state)
        $salon = @($state.rooms) | Where-Object room -eq 'salon' | Select-Object -First 1
        $state.mode -eq 'normal' -and $salon.source_status -eq 'online' -and $salon.status -eq 'applied'
    }
    if ('Świeży pomiar salonu wrócił' -notin @($restored.timeline.title)) {
        throw 'Timeline did not prove restoration by a fresh reading.'
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "junexis run exited with code $($process.ExitCode)." }
    Write-Output "PASS: Dom 7 proved silence timeout, limited mode, preserved state and fresh-reading restoration at $panelUrl"
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

