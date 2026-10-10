param(
    [Parameter(Mandatory = $true)]
    [string]$ServerExecutable,
    [Parameter(Mandatory = $true)]
    [string]$ReportDirectory,
    [int]$HttpPort = 18100,
    [int]$HealthPort = 18101,
    [int]$TcpBasePort = 19100,
    [int]$TimeoutSeconds = 15,
    [string]$FirstBootId = "boot-alpha",
    [string]$SecondBootId = "boot-beta",
    [int]$ExpectedSecondGeneration = 1,
    [string]$ThirdBootId = ""
)

$ErrorActionPreference = "Stop"
$serverPath = (Resolve-Path -LiteralPath $ServerExecutable).Path
$reportPath = (Resolve-Path -LiteralPath $ReportDirectory).Path
$ownedRoot = Join-Path ([IO.Path]::GetTempPath()) "junexis-three-environment-home-test"
$stateDirectory = Join-Path $ownedRoot ([Guid]::NewGuid().ToString("N"))
$server = $null
$report = $null

$requiredPanelFiles = @(
    "index.html",
    "report.css",
    "serve.mjs",
    "junexis-report-manifest.json",
    "dist/browser.js"
)
foreach ($relativePath in $requiredPanelFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $reportPath $relativePath) -PathType Leaf)) {
        throw "Wygenerowana aplikacja @report nie zawiera pliku $relativePath."
    }
}
$reportManifest = Get-Content -LiteralPath (Join-Path $reportPath "junexis-report-manifest.json") -Raw | ConvertFrom-Json
if ($reportManifest.schemaVersion -ne "junexis.report-application/0.1" -or
    $reportManifest.renderer -ne "declarative" -or
    $reportManifest.historyLimit -ne 50) {
    throw "Manifest aplikacji @report nie spełnia kontraktu M10.1."
}

function Start-Server {
    $start = [Diagnostics.ProcessStartInfo]::new($serverPath)
    $start.WorkingDirectory = $stateDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @(
        "--server-mode", "persistent", "--http-port", [string]$HttpPort,
        "--health-port", [string]$HealthPort, "--tcp-port", [string]$TcpBasePort
    )) {
        $start.ArgumentList.Add($argument)
    }
    return [Diagnostics.Process]::Start($start)
}

function Start-Report {
    $start = [Diagnostics.ProcessStartInfo]::new("node")
    $start.WorkingDirectory = $reportPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add("dist/deploy.js")
    $start.Environment["JUNEXIS_SERVER_URL"] = "http://127.0.0.1:$HttpPort"
    return [Diagnostics.Process]::Start($start)
}

function Stop-ProcessTree([Diagnostics.Process]$Process) {
    if ($null -ne $Process -and -not $Process.HasExited) {
        $Process.Kill($true)
        $Process.WaitForExit()
    }
}

function Wait-ForHttp {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$HealthPort/health/ready"
            if ($response.StatusCode -eq 200) { return }
        } catch {
            # Serwer może jeszcze otwierać port.
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Koordynator domu nie osiągnął gotowości."
}

function Send-DeviceHeartbeat([string]$BootId, [int]$Sequence) {
    $payload = [ordered]@{
        device_id = "living-room-sensor"
        boot_id = $BootId
        sequence = $Sequence
        safe_state = $true
        observed_at = [DateTimeOffset]::UtcNow.ToString("O")
        _ts_us = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() * 1000
    } | ConvertTo-Json -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $length = [byte[]]@(
        (($bytes.Length -shr 24) -band 0xff),
        (($bytes.Length -shr 16) -band 0xff),
        (($bytes.Length -shr 8) -band 0xff),
        ($bytes.Length -band 0xff)
    )
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect("127.0.0.1", $TcpBasePort + 1)
        $stream = $client.GetStream()
        $stream.Write($length, 0, $length.Length)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.WriteByte(10)
        $stream.Flush()
    } finally {
        $client.Dispose()
    }
}

function Test-HeartbeatRejectedWhileServerIsDown([string]$BootId, [int]$Sequence) {
    try {
        Send-DeviceHeartbeat -BootId $BootId -Sequence $Sequence
        return $false
    } catch [Net.Sockets.SocketException] {
        return $true
    }
}

function Wait-ForState(
    [string]$BootId,
    [int]$Generation,
    [int]$Sequence,
    [string]$Status
) {
    $statePath = Join-Path $stateDirectory "HomeCoordinator.state.json"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $statePath) {
            try {
                $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
                if ($state.last_boot_id -eq $BootId -and
                    $state.connection_generation -eq $Generation -and
                    $state.last_sequence -eq $Sequence -and
                    $state.last_status -eq $Status) {
                    return $state
                }
            } catch {
                # Zapis atomowy może być właśnie wymieniany.
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Stan koordynatora nie osiągnął boot_id=$BootId, generation=$Generation, sequence=$Sequence, status=$Status."
}

try {
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    $server = Start-Server
    Wait-ForHttp
    $report = Start-Report
    Start-Sleep -Milliseconds 800

    # Najpierw dowodzimy zwykłego, zdrowego przepływu przez wszystkie granice.
    Send-DeviceHeartbeat -BootId $FirstBootId -Sequence 1
    $first = Wait-ForState -BootId $FirstBootId -Generation 0 -Sequence 1 -Status "online"
    Send-DeviceHeartbeat -BootId $FirstBootId -Sequence 2
    $normal = Wait-ForState -BootId $FirstBootId -Generation 0 -Sequence 2 -Status "online"
    Start-Sleep -Milliseconds 500

    # Dopiero teraz zatrzymujemy środkowy proces. Raport pozostaje uruchomiony.
    Stop-ProcessTree $server
    $server.Dispose()
    $server = $null
    Start-Sleep -Milliseconds 400
    $deliveryRejected = Test-HeartbeatRejectedWhileServerIsDown -BootId $SecondBootId -Sequence 1
    if (-not $deliveryRejected) {
        throw "Heartbeat został przyjęty mimo zatrzymanego koordynatora."
    }

    # Ten sam katalog roboczy oznacza tę samą trwałą odpowiedzialność serwera.
    $server = Start-Server
    Wait-ForHttp
    $restored = Wait-ForState -BootId $FirstBootId -Generation 0 -Sequence 2 -Status "online"
    Start-Sleep -Milliseconds 800

    Send-DeviceHeartbeat -BootId $SecondBootId -Sequence 1
    $second = Wait-ForState -BootId $SecondBootId -Generation $ExpectedSecondGeneration -Sequence 1 -Status "rejoined"
    Start-Sleep -Milliseconds 1200

    $third = $null
    if ($ThirdBootId -ne "") {
        Send-DeviceHeartbeat -BootId $ThirdBootId -Sequence 1
        $third = Wait-ForState -BootId $ThirdBootId -Generation ($ExpectedSecondGeneration + 1) -Sequence 1 -Status "rejoined"
        Start-Sleep -Milliseconds 600
    }

    Stop-ProcessTree $report
    $reportOutput = $report.StandardOutput.ReadToEnd() + $report.StandardError.ReadToEnd()
    if ($reportOutput -notmatch [regex]::Escape($FirstBootId) -or
        $reportOutput -notmatch [regex]::Escape($SecondBootId) -or
        $reportOutput -notmatch "first-heartbeat" -or
        $reportOutput -notmatch "heartbeat-confirmed" -or
        $reportOutput -notmatch "reconnecting" -or
        $reportOutput -notmatch "connection_generation: $ExpectedSecondGeneration" -or
        $reportOutput -notmatch "status: 'rejoined'" -or
        ($ThirdBootId -ne "" -and $reportOutput -notmatch [regex]::Escape($ThirdBootId))) {
        throw "Raport nie potwierdził normalnego przepływu, reconnectu i ponownego dołączenia.`n$reportOutput"
    }

    [pscustomobject]@{
        result = "PASS"
        embedded_server_contract = "tcp-json-framed"
        server_report_contract = "websocket-messagepack"
        first_boot = $first.last_boot_id
        first_status = $first.last_status
        normal_heartbeat_confirmed = ($normal.last_sequence -eq 2)
        initial_connection_generation = $normal.connection_generation
        coordinator_stopped = $true
        delivery_rejected_while_server_down = $deliveryRejected
        coordinator_state_restored = ($restored.last_boot_id -eq $FirstBootId)
        second_boot = $second.last_boot_id
        second_status = $second.last_status
        second_connection_generation = $second.connection_generation
        report_reconnected = $true
        generated_panel_artifact = $true
        panel_contract = $reportManifest.schemaVersion
        panel_renderer = $reportManifest.renderer
        panel_history_limit = $reportManifest.historyLimit
        third_boot = if ($null -ne $third) { $third.last_boot_id } else { "" }
        third_connection_generation = if ($null -ne $third) { $third.connection_generation } else { 0 }
    } | ConvertTo-Json
} finally {
    Stop-ProcessTree $report
    Stop-ProcessTree $server
    if ($null -ne $report) { $report.Dispose() }
    if ($null -ne $server) { $server.Dispose() }
    if (Test-Path -LiteralPath $stateDirectory) {
        $resolvedState = (Resolve-Path -LiteralPath $stateDirectory).Path
        $resolvedOwnedRoot = [IO.Path]::GetFullPath($ownedRoot)
        if (-not $resolvedState.StartsWith($resolvedOwnedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Odmowa usunięcia nieoczekiwanego katalogu: $resolvedState"
        }
        Remove-Item -LiteralPath $resolvedState -Recurse -Force
    }
}
