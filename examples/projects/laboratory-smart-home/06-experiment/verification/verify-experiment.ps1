[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$langRoot = (Resolve-Path (Join-Path $projectRoot '../../../..')).Path
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workRoot = Join-Path $tempRoot "junexis-home-experiment-$([Guid]::NewGuid().ToString('N'))"
$stdoutPath = Join-Path $workRoot 'stdout.log'
$stderrPath = Join-Path $workRoot 'stderr.log'
$process = $null

function Get-State([string]$PanelUrl) {
    return Invoke-RestMethod -Uri ($PanelUrl + 'api/home')
}

function Set-Profile([string]$PanelUrl, [double]$Threshold, [int]$Delay, [int]$MaxAge, [string]$Profile, [string]$Hypothesis) {
    $body = @{
        threshold = $Threshold
        communication_delay_ms = $Delay
        max_age_ms = $MaxAge
        profile = $Profile
        hypothesis = $Hypothesis
    } | ConvertTo-Json
    return Invoke-RestMethod -Method Post -ContentType 'application/json' -Body $body -Uri ($PanelUrl + 'api/experiment')
}

function Wait-ForRoom([string]$PanelUrl, [int]$Revision, [string]$RoomName, [scriptblock]$Predicate, [int]$TimeoutSeconds = 16) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $state = Get-State $PanelUrl
        $room = @($state.rooms) | Where-Object { $_.room -eq $RoomName -and $_.experiment_revision -eq $Revision } | Select-Object -First 1
        if ($room -and (& $Predicate $room $state)) { return $room }
    }
    throw "Timed out waiting for room '$RoomName' in experiment revision $Revision."
}

New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $cliProject, '--no-build', '--',
        'run', $projectRoot, '--duration', '70'
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
    if (-not $panelUrl) { throw 'Experiment panel did not publish its local URL.' }

    $page = Invoke-WebRequest -UseBasicParsing -Uri $panelUrl
    foreach ($phrase in @('Eksperymentuj', 'Najpierw hipoteza', 'Próg 1400', 'Granica świeżości', 'Uruchom eksperyment')) {
        if (-not $page.Content.Contains($phrase)) { throw "Experiment panel is missing phrase: $phrase" }
    }

    $baseline = Get-State $panelUrl
    if ($baseline.config.threshold -ne 1000 -or $baseline.config.communication_delay_ms -ne 0 -or $baseline.config.max_age_ms -ne 6000) {
        throw 'Baseline configuration is not the documented safe profile.'
    }
    $baselineRoom = Wait-ForRoom $panelUrl $baseline.config.revision 'salon' { param($room) $room.air_quality -eq 1250 -and $room.decision -eq 'enable-ventilation' -and $room.fresh }
    if (-not $baselineRoom.actual_enabled) { throw 'Baseline did not confirm ventilation for 1250 ppm.' }

    $thresholdState = Set-Profile $panelUrl 1400 0 6000 'wyższy-próg' '1250 przejdzie do obserwacji.'
    $thresholdRoom = Wait-ForRoom $panelUrl $thresholdState.config.revision 'salon' { param($room) $room.air_quality -eq 1250 -and $room.decision -eq 'observe' -and $room.fresh }
    if ($thresholdRoom.actual_enabled) { throw 'Threshold experiment did not turn ventilation off for 1250 ppm.' }
    [void](Wait-ForRoom $panelUrl $thresholdState.config.revision 'kuchnia' { param($room) $room.air_quality -eq 1650 -and $room.decision -eq 'enable-ventilation' })

    $delayState = Set-Profile $panelUrl 1000 750 6000 'wolniej' 'Wiek wzrośnie, decyzja pozostanie.'
    $delayRoom = Wait-ForRoom $panelUrl $delayState.config.revision 'kuchnia' { param($room) $room.air_quality -eq 1650 -and $room.decision -eq 'enable-ventilation' -and $room.fresh -and $room.observation_age_ms -ge 700 } 20
    if ($delayRoom.status -ne 'applied') { throw 'Delayed fresh reading was not applied.' }

    $freshnessState = Set-Profile $panelUrl 1000 1500 500 'świeżość' 'Pomiar przekroczy budżet.'
    $heldRoom = Wait-ForRoom $panelUrl $freshnessState.config.revision 'salon' { param($room) -not $room.fresh -and $room.decision -eq 'hold-stale-reading' } 24
    if ($heldRoom.status -ne 'held-stale-reading' -or $heldRoom.observation_age_ms -le $heldRoom.max_age_ms) {
        throw 'Freshness experiment did not preserve the explicit hold evidence.'
    }

    $reset = Invoke-RestMethod -Method Post -ContentType 'application/json' -Body '{}' -Uri ($panelUrl + 'api/reset')
    if ($reset.config.profile -ne 'bazowy' -or $reset.config.threshold -ne 1000 -or $reset.config.communication_delay_ms -ne 0 -or $reset.config.max_age_ms -ne 6000) {
        throw 'Reset did not restore the safe baseline profile.'
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "junexis run exited with code $($process.ExitCode)." }
    Write-Output "PASS: Dom 6 verified baseline, threshold, delay, freshness hold and reset at $panelUrl"
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

