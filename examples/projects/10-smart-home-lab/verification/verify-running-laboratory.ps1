param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [int]$TimeoutSeconds = 12,
    [string]$NarrativeUrl = "",
    [string]$ExpectedBuildId = "",
    [string]$ExpectedBundleId = ""
)

$ErrorActionPreference = "Stop"

function Read-State {
    Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/laboratory/state"
}

function Wait-ForState([scriptblock]$Condition, [string]$Description) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $state = Read-State
        if (& $Condition $state) {
            return $state
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Nie osiągnięto punktu kontrolnego: $Description"
}

$panel = Invoke-WebRequest -UseBasicParsing -Uri "$BaseUrl/laboratory/"
if ($panel.StatusCode -ne 200 -or
    -not $panel.Content.Contains("Dom, który zauważa i reaguje") -or
    -not $panel.Content.Contains("Oś przyczyn") -or
    -not $panel.Content.Contains("Postaw hipotezę") -or
    -not $panel.Content.Contains("Jawna polityka recovery i dowodów") -or
    -not $panel.Content.Contains("Próba przeciążenia")) {
    throw "Panel laboratorium nie jest dostępny lub ma nieoczekiwaną treść."
}

$automatic = Wait-ForState {
    param($state)
    $state.execution_mode -eq "simulation" -and
        -not $state.hardware_connected -and
        $state.home_started -and
        $state.rooms.Count -eq 3 -and
        $state.ventilation_enabled -and
        $state.sensor_loss_detected -and
        $state.safe_state_selected -and
        $state.sensor_recovered
} "pełny scenariusz automatyczny"

$narrative = $null
if ($NarrativeUrl -ne "") {
    $null = Invoke-RestMethod -Method Post -ContentType "application/json" `
        -Body '{"mode":"Debug"}' -Uri "$NarrativeUrl/sampling/mode"
    $narrative = Wait-ForState {
        param($state)
        $state.narrative_connected -and
            $state.narrative_depth -ge 3 -and
            $state.narrative_tasks -contains "HomeScenario" -and
            $state.narrative_tasks -contains "HomeDecisionEngine" -and
            $state.narrative_tasks -contains "HomeObserver" -and
            ($ExpectedBuildId -eq "" -or $state.narrative_build_id -eq $ExpectedBuildId) -and
            ($ExpectedBundleId -eq "" -or $state.narrative_bundle_id -eq $ExpectedBundleId)
    } "rekonstrukcja Narrative z poprawną tożsamością buildu"
}

$decisionEvent = $automatic.timeline | Where-Object {
    $_.kind -eq "decision" -and
        $_.cause -eq "air-quality-over-configured-threshold" -and
        $_.effect -eq "enable-ventilation"
} | Select-Object -First 1
if ($null -eq $decisionEvent) {
    throw "Oś przyczyn nie połączyła reguły jakości powietrza z włączeniem wentylacji."
}

$null = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"active":true}' -Uri "$BaseUrl/api/laboratory/fault"

$forced = Wait-ForState {
    param($state)
    $state.fault_forced -and
        -not $state.sensor_online -and
        $state.last_decision -eq "sensor-unavailable" -and
        $state.last_action -eq "keep-ventilation-safe"
} "ręcznie wymuszona awaria i stan bezpieczny"

$reset = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{}' -Uri "$BaseUrl/api/laboratory/reset"

if ($reset.fault_forced -or $reset.home_started -or $reset.sequence -ne 0) {
    throw "Reset nie przywrócił znanego stanu początkowego."
}

$experiment = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"air_quality_threshold":1400,"communication_delay_ms":500,"failure_policy":"pause-automation"}' `
    -Uri "$BaseUrl/api/laboratory/experiment"
if ($experiment.stage -ne "eksperymentuj" -or
    $experiment.air_quality_threshold -ne 1400 -or
    $experiment.communication_delay_ms -ne 500 -or
    $experiment.failure_policy -ne "pause-automation") {
    throw "Konfiguracja eksperymentu nie została zastosowana."
}

$experimentDecision = Wait-ForState {
    param($state)
    $state.stage -eq "eksperymentuj" -and
        $state.air_quality -eq 1250 -and
        $state.last_action -eq "observe"
} "próg 1400 ppm zmienia decyzję dla pomiaru 1250 ppm"

$null = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"active":true}' -Uri "$BaseUrl/api/laboratory/fault"
$experimentFault = Wait-ForState {
    param($state)
    $state.fault_forced -and
        $state.last_action -eq "pause-automation"
} "eksperymentalna polityka utraty czujnika"

$experimentReset = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{}' -Uri "$BaseUrl/api/laboratory/reset"
if ($experimentReset.stage -ne "poznaj" -or
    $experimentReset.air_quality_threshold -ne 1000 -or
    $experimentReset.communication_delay_ms -ne 0 -or
    $experimentReset.failure_policy -ne "keep-ventilation-safe") {
    throw "Reset nie przywrócił bezpiecznego profilu Poznaj."
}

$automaticControl = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"air_quality_threshold":900,"communication_delay_ms":0,"failure_policy":"keep-ventilation-safe","recovery_mode":"automatic","recovery_delay_ms":750,"evidence_limit":12}' `
    -Uri "$BaseUrl/api/laboratory/control"
$null = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"active":true}' -Uri "$BaseUrl/api/laboratory/fault"
$automaticRecovery = Wait-ForState {
    param($state)
    $state.stage -eq "przejmij-kontrole" -and
        -not $state.fault_forced -and
        -not $state.recovery_pending -and
        $state.timeline.Count -le 12 -and
        ($state.timeline | Where-Object { $_.cause -eq "automatic-recovery-policy" }).Count -gt 0
} "automatyczne recovery i limit dowodów"

$manualControl = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"air_quality_threshold":1000,"communication_delay_ms":0,"failure_policy":"pause-automation","recovery_mode":"manual-approval","recovery_delay_ms":2000,"evidence_limit":12}' `
    -Uri "$BaseUrl/api/laboratory/control"
$null = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"active":true}' -Uri "$BaseUrl/api/laboratory/fault"
$rejectedRecovery = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"approved":false}' -Uri "$BaseUrl/api/laboratory/recover"
Start-Sleep -Milliseconds 1000
$stillFaulted = Read-State
if (-not $rejectedRecovery.recovery_pending -or -not $stillFaulted.fault_forced) {
    throw "Odrzucone ręczne recovery nie utrzymało kontrolowanej awarii."
}
$approvedRecovery = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"approved":true}' -Uri "$BaseUrl/api/laboratory/recover"
if ($approvedRecovery.fault_forced -or -not $approvedRecovery.recovery_pending -or
    $approvedRecovery.recovery_fresh_evidence -ne 0) {
    throw "Zatwierdzone ręczne recovery nie rozpoczęło walidacji świeżych danych."
}
$approvedRecovery = Wait-ForState {
    param($state)
    -not $state.fault_forced -and -not $state.recovery_pending -and
        $state.recovery_fresh_evidence -ge $state.recovery_required_evidence
} "dwa świeże pomiary po zatwierdzeniu ręcznego recovery"
$overload = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{"burst_size":24,"buffer_limit":8,"consumer_delay_ms":40}' `
    -Uri "$BaseUrl/api/laboratory/overload"
if (-not $overload.overload_active -or
    $overload.overload_accepted -ne 8 -or
    $overload.overload_dropped -ne 16) {
    throw "Ograniczony bufor nie wymusił oczekiwanego backpressure."
}
$overloadRecovery = Wait-ForState {
    param($state)
    -not $state.overload_active -and
        $state.overload_recovered -and
        $state.overload_processed -eq 8 -and
        ($state.timeline | Where-Object { $_.cause -eq "bounded-queue-drained" }).Count -gt 0
} "opróżnienie ograniczonego bufora po przeciążeniu"
$controlReset = Invoke-RestMethod -Method Post -ContentType "application/json" `
    -Body '{}' -Uri "$BaseUrl/api/laboratory/reset"
if ($controlReset.recovery_mode -ne "manual-approval" -or
    $controlReset.recovery_delay_ms -ne 2000 -or
    $controlReset.evidence_limit -ne 18) {
    throw "Reset nie przywrócił bezpiecznych ustawień recovery i dowodów."
}

[pscustomobject]@{
    result = "PASS"
    execution_mode = $automatic.execution_mode
    hardware_connected = $automatic.hardware_connected
    panel = $panel.StatusCode
    automatic_sequence = $automatic.sequence
    rooms = $automatic.rooms.Count
    causal_event = $decisionEvent.event_id
    narrative_connected = $null -ne $narrative
    narrative_depth = if ($null -ne $narrative) { $narrative.narrative_depth } else { 0 }
    narrative_event = if ($null -ne $narrative) { $narrative.narrative_event_id } else { 0 }
    forced_decision = $forced.last_decision
    forced_action = $forced.last_action
    reset_sequence = $reset.sequence
    experiment_threshold = $experiment.air_quality_threshold
    experiment_delay_ms = $experiment.communication_delay_ms
    experiment_decision = $experimentDecision.last_action
    experiment_fault_action = $experimentFault.last_action
    experiment_reset_stage = $experimentReset.stage
    automatic_recovery_mode = $automaticControl.recovery_mode
    automatic_recovery_completed = -not $automaticRecovery.fault_forced
    automatic_evidence_count = $automaticRecovery.timeline.Count
    manual_recovery_mode = $manualControl.recovery_mode
    rejected_recovery_pending = $rejectedRecovery.recovery_pending
    approved_recovery_completed = -not $approvedRecovery.fault_forced
    overload_accepted = $overload.overload_accepted
    overload_dropped = $overload.overload_dropped
    overload_recovered = $overloadRecovery.overload_recovered
    durable_evidence = $overloadRecovery.evidence_persistent
    control_reset_mode = $controlReset.recovery_mode
} | ConvertTo-Json
