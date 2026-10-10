[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$langRoot = (Resolve-Path (Join-Path $projectRoot '../../../..')).Path
$cliProject = Join-Path $langRoot 'compiler/JunexisLang.Compiler.CLI/JunexisLang.Compiler.CLI.csproj'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$workRoot = Join-Path $tempRoot "junexis-home-panel-$([Guid]::NewGuid().ToString('N'))"
$stdoutPath = Join-Path $workRoot 'stdout.log'
$stderrPath = Join-Path $workRoot 'stderr.log'
$process = $null

New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', $cliProject, '--no-build', '--',
        'run', $projectRoot, '--duration', '12'
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
    if (-not $panelUrl) { throw 'Panel did not publish its local URL.' }

    $page = Invoke-WebRequest -UseBasicParsing -Uri $panelUrl
    if ($page.StatusCode -ne 200 -or
        -not $page.Content.Contains('Dom Junexis') -or
        -not $page.Content.Contains('Dlaczego?')) {
        throw 'Panel HTML does not expose the expected learner view.'
    }

    $observed = [Collections.Generic.HashSet[int]]::new()
    $stateDeadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $stateDeadline -and $observed.Count -lt 2) {
        Start-Sleep -Milliseconds 250
        $state = Invoke-RestMethod -Uri ($panelUrl + 'api/home')
        if ($state.status -eq 'applied') {
            [void]$observed.Add([int]$state.air_quality)
            if ($state.threshold -ne 1000 -or $state.requested_enabled -ne $state.actual_enabled) {
                throw 'Panel state does not preserve threshold or execution confirmation.'
            }
        }
    }
    if (-not $observed.Contains(420) -or -not $observed.Contains(1250)) {
        throw "Panel did not expose both expected measurements: $($observed -join ', ')."
    }

    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "junexis run exited with code $($process.ExitCode)."
    }
    Write-Output "PASS: panel served HTML and confirmed 420/1250 ppm states at $panelUrl"
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
