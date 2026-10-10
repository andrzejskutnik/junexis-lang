param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [int]$HttpPort = 18090,
    [int]$HealthPort = 18091,
    [int]$TcpPort = 19010,
    [int]$TimeoutSeconds = 12
)

$ErrorActionPreference = "Stop"
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$ownedRoot = Join-Path ([IO.Path]::GetTempPath()) "junexis-laboratory-restart-test"
$stateDirectory = Join-Path $ownedRoot ([Guid]::NewGuid().ToString("N"))
$baseUrl = "http://127.0.0.1:$HttpPort"
$server = $null

function Start-LaboratoryServer {
    $start = [Diagnostics.ProcessStartInfo]::new($executablePath)
    $start.WorkingDirectory = Split-Path $executablePath -Parent
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.ArgumentList.Add("--server-mode")
    $start.ArgumentList.Add("persistent")
    $start.ArgumentList.Add("--http-port")
    $start.ArgumentList.Add([string]$HttpPort)
    $start.ArgumentList.Add("--health-port")
    $start.ArgumentList.Add([string]$HealthPort)
    $start.ArgumentList.Add("--tcp-port")
    $start.ArgumentList.Add([string]$TcpPort)
    $start.Environment["JUNEXIS_LAB_STATE_DIR"] = $stateDirectory
    return [Diagnostics.Process]::Start($start)
}

function Stop-LaboratoryServer {
    if ($null -ne $script:server -and -not $script:server.HasExited) {
        $script:server.Kill($true)
        $script:server.WaitForExit()
    }
    if ($null -ne $script:server) {
        $script:server.Dispose()
        $script:server = $null
    }
}

function Wait-ForState([scriptblock]$Condition, [string]$Description) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $state = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/laboratory/state"
            if (& $Condition $state) {
                return $state
            }
        } catch [System.Net.Http.HttpRequestException] {
            # Proces może jeszcze otwierać port.
        } catch [System.Net.WebException] {
            # Windows PowerShell zgłasza brak gotowości jako WebException.
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Nie osiągnięto punktu kontrolnego: $Description"
}

try {
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    $server = Start-LaboratoryServer
    $null = Wait-ForState { param($state) $state.evidence_persistent } "trwały magazyn dowodów"

    $null = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body '{"air_quality_threshold":1337,"communication_delay_ms":0,"failure_policy":"pause-automation","recovery_mode":"manual-approval","recovery_delay_ms":2000,"evidence_limit":30}' `
        -Uri "$baseUrl/api/laboratory/control"
    $null = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body '{"active":true}' -Uri "$baseUrl/api/laboratory/fault"
    $interruptedOverload = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body '{"burst_size":24,"buffer_limit":8,"consumer_delay_ms":500}' `
        -Uri "$baseUrl/api/laboratory/overload"
    if (-not $interruptedOverload.overload_active -or
        $interruptedOverload.overload_accepted -ne 8 -or
        $interruptedOverload.overload_dropped -ne 16) {
        throw "Nie przygotowano ograniczonej kolejki do próby restartu."
    }
    $beforeRestart = Wait-ForState {
        param($state)
        $state.fault_forced -and $state.recovery_pending -and
            $state.air_quality_threshold -eq 1337 -and $state.overload_active -and
            $state.overload_accepted -eq 8 -and $state.overload_dropped -eq 16 -and
            $state.timeline.Count -gt 0
    } "punkt kontrolny przed restartem"

    Stop-LaboratoryServer
    $server = Start-LaboratoryServer
    $afterRestart = Wait-ForState {
        param($state)
        $state.restored_after_restart -and
            $state.execution_mode -eq "simulation" -and
            -not $state.hardware_connected -and
            $state.restored_event_count -gt 0 -and
            $state.fault_forced -and $state.recovery_pending -and
            $state.air_quality_threshold -eq 1337 -and
            -not $state.overload_active -and $state.overload_recovered -and
            $state.overload_accepted -eq 8 -and $state.overload_dropped -eq 16 -and
            ($state.timeline | Where-Object { $_.cause -eq "process-restart" -and $_.effect -eq "transient-buffer-closed" }).Count -gt 0 -and
            ($state.timeline | Where-Object { $_.cause -eq "durable-state-loaded" }).Count -gt 0
    } "odtworzenie konfiguracji, awarii i dowodów po restarcie procesu"

    $recovered = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body '{"approved":true}' -Uri "$baseUrl/api/laboratory/recover"
    if ($recovered.fault_forced -or -not $recovered.recovery_pending -or
        $recovered.recovery_fresh_evidence -ne 0) {
        throw "Recovery po restarcie nie rozpoczęło walidacji świeżych danych."
    }
    $recovered = Wait-ForState {
        param($state)
        -not $state.fault_forced -and -not $state.recovery_pending -and
            $state.recovery_fresh_evidence -ge $state.recovery_required_evidence
    } "dwa świeże pomiary po recovery zatwierdzonym w nowym procesie"

    [pscustomobject]@{
        result = "PASS"
        execution_mode = $afterRestart.execution_mode
        hardware_connected = $afterRestart.hardware_connected
        evidence_file = $afterRestart.evidence_path
        events_before_restart = $beforeRestart.timeline.Count
        restored_event_count = $afterRestart.restored_event_count
        restored_threshold = $afterRestart.air_quality_threshold
        restored_fault = $afterRestart.fault_forced
        interrupted_overload_closed = $afterRestart.overload_recovered
        overload_accepted_before_restart = $afterRestart.overload_accepted
        overload_dropped_before_restart = $afterRestart.overload_dropped
        recovery_after_restart = -not $recovered.fault_forced
    } | ConvertTo-Json
} finally {
    Stop-LaboratoryServer
    if (Test-Path -LiteralPath $stateDirectory) {
        $resolvedState = (Resolve-Path -LiteralPath $stateDirectory).Path
        $resolvedOwnedRoot = [IO.Path]::GetFullPath($ownedRoot)
        if (-not $resolvedState.StartsWith($resolvedOwnedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Odmowa usunięcia nieoczekiwanego katalogu: $resolvedState"
        }
        Remove-Item -LiteralPath $resolvedState -Recurse -Force
    }
}
