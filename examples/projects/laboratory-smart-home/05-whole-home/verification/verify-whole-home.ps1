[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$langRoot = (Resolve-Path (Join-Path $projectRoot '../../../..')).Path
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workRoot = Join-Path $tempRoot "junexis-whole-home-$([Guid]::NewGuid().ToString('N'))"
$stdoutPath = Join-Path $workRoot 'stdout.log'
$stderrPath = Join-Path $workRoot 'stderr.log'
$process = $null

New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $cliProject, '--no-build', '--',
        'run', $projectRoot, '--duration', '18'
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
    if (-not $panelUrl) { throw 'Whole-home panel did not publish its local URL.' }

    $page = Invoke-WebRequest -UseBasicParsing -Uri $panelUrl
    foreach ($phrase in @('Cały dom', 'Salon', 'Kuchnia', 'Sypialnia', 'Najwyższy pomiar')) {
        if (-not $page.Content.Contains($phrase)) {
            throw "Panel HTML is missing learner-visible phrase: $phrase"
        }
    }

    $expected = @{
        salon = [Collections.Generic.HashSet[int]]::new()
        kuchnia = [Collections.Generic.HashSet[int]]::new()
        sypialnia = [Collections.Generic.HashSet[int]]::new()
    }
    $sawCompleteProjection = $false
    $sawVentilation = $false
    $stateDeadline = [DateTime]::UtcNow.AddSeconds(14)
    while ([DateTime]::UtcNow -lt $stateDeadline) {
        Start-Sleep -Milliseconds 200
        $houseState = Invoke-RestMethod -Uri ($panelUrl + 'api/home')
        if ($houseState.summary.known_rooms -eq 3) { $sawCompleteProjection = $true }
        if ($houseState.summary.ventilating_rooms -gt 0) { $sawVentilation = $true }
        foreach ($room in @($houseState.rooms)) {
            if ($room.room -in $expected.Keys) {
                [void]$expected[$room.room].Add([int]$room.air_quality)
                if ($room.threshold -ne 1000 -or $room.requested_enabled -ne $room.actual_enabled) {
                    throw "Room '$($room.room)' lost its threshold or execution confirmation."
                }
            }
        }
        if ($expected.salon.Contains(420) -and $expected.salon.Contains(1250) -and
            $expected.kuchnia.Contains(780) -and $expected.kuchnia.Contains(1650) -and
            $expected.sypialnia.Contains(560) -and $expected.sypialnia.Contains(920) -and
            $sawCompleteProjection -and $sawVentilation) {
            break
        }
    }

    if (-not $sawCompleteProjection) { throw 'Panel never composed all three room states.' }
    if (-not $sawVentilation) { throw 'Panel never showed independently active ventilation.' }
    foreach ($contract in @(
        @{ Room = 'salon'; Values = @(420, 1250) },
        @{ Room = 'kuchnia'; Values = @(780, 1650) },
        @{ Room = 'sypialnia'; Values = @(560, 920) }
    )) {
        foreach ($value in $contract.Values) {
            if (-not $expected[$contract.Room].Contains($value)) {
                throw "Room '$($contract.Room)' did not expose expected measurement $value ppm."
            }
        }
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "junexis run exited with code $($process.ExitCode)."
    }
    Write-Output "PASS: whole-home panel composed three rooms and preserved all six expected states at $panelUrl"
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
