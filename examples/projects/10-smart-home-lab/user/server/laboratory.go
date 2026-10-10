package user

import (
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"
)

type sensorReading struct {
	Room       string  `json:"room"`
	Celsius    float64 `json:"celsius"`
	Humidity   float64 `json:"humidity"`
	AirQuality float64 `json:"air_quality"`
	Motion     bool    `json:"motion"`
	Online     bool    `json:"online"`
	Sequence   int64   `json:"sequence"`
}

type homeDecision struct {
	DecisionID string `json:"decision_id"`
	Room       string `json:"room"`
	Rule       string `json:"rule"`
	Reason     string `json:"reason"`
	Action     string `json:"action"`
	Sequence   int64  `json:"sequence"`
}

type faultCommand struct {
	Active bool `json:"active"`
}

type experimentCommand struct {
	AirQualityThreshold float64 `json:"air_quality_threshold"`
	CommunicationDelay  int64   `json:"communication_delay_ms"`
	FailurePolicy       string  `json:"failure_policy"`
}

type controlCommand struct {
	AirQualityThreshold float64 `json:"air_quality_threshold"`
	CommunicationDelay  int64   `json:"communication_delay_ms"`
	FailurePolicy       string  `json:"failure_policy"`
	RecoveryMode        string  `json:"recovery_mode"`
	RecoveryDelay       int64   `json:"recovery_delay_ms"`
	EvidenceLimit       int64   `json:"evidence_limit"`
}

type overloadCommand struct {
	BurstSize     int64 `json:"burst_size"`
	BufferLimit   int64 `json:"buffer_limit"`
	ConsumerDelay int64 `json:"consumer_delay_ms"`
}

type recoveryCommand struct {
	Approved bool `json:"approved"`
}

type roomState struct {
	Room       string  `json:"room"`
	Celsius    float64 `json:"celsius"`
	Humidity   float64 `json:"humidity"`
	AirQuality float64 `json:"air_quality"`
	Motion     bool    `json:"motion"`
	Online     bool    `json:"online"`
	Sequence   int64   `json:"sequence"`
}

type laboratoryEvent struct {
	EventID    string `json:"event_id"`
	Kind       string `json:"kind"`
	Room       string `json:"room"`
	Title      string `json:"title"`
	Detail     string `json:"detail"`
	Cause      string `json:"cause"`
	Effect     string `json:"effect"`
	Sequence   int64  `json:"sequence"`
	OccurredAt string `json:"occurred_at"`
}

type traceEvent struct {
	EventID    int64             `json:"eventId"`
	Function   string            `json:"function"`
	Attributes map[string]string `json:"attributes"`
}

type whyResult struct {
	Depth       int64        `json:"depth"`
	CausalChain []traceEvent `json:"causalChain"`
}

type laboratoryState struct {
	Scenario                 string            `json:"scenario"`
	ExecutionMode            string            `json:"execution_mode"`
	HardwareConnected        bool              `json:"hardware_connected"`
	Stage                    string            `json:"stage"`
	SensorOnline             bool              `json:"sensor_online"`
	FaultForced              bool              `json:"fault_forced"`
	AirQualityThreshold      float64           `json:"air_quality_threshold"`
	CommunicationDelay       int64             `json:"communication_delay_ms"`
	FailurePolicy            string            `json:"failure_policy"`
	ExperimentRevision       int64             `json:"experiment_revision"`
	RecoveryMode             string            `json:"recovery_mode"`
	RecoveryDelay            int64             `json:"recovery_delay_ms"`
	EvidenceLimit            int64             `json:"evidence_limit"`
	ControlRevision          int64             `json:"control_revision"`
	RecoveryPending          bool              `json:"recovery_pending"`
	RecoveryFreshEvidence    int64             `json:"recovery_fresh_evidence"`
	RecoveryRequiredEvidence int64             `json:"recovery_required_evidence"`
	EvidencePersistent       bool              `json:"evidence_persistent"`
	EvidencePath             string            `json:"evidence_path"`
	RestoredAfterRestart     bool              `json:"restored_after_restart"`
	RestoredEventCount       int64             `json:"restored_event_count"`
	OverloadActive           bool              `json:"overload_active"`
	OverloadBurstSize        int64             `json:"overload_burst_size"`
	OverloadBufferLimit      int64             `json:"overload_buffer_limit"`
	OverloadDelay            int64             `json:"overload_consumer_delay_ms"`
	OverloadAccepted         int64             `json:"overload_accepted"`
	OverloadDropped          int64             `json:"overload_dropped"`
	OverloadProcessed        int64             `json:"overload_processed"`
	OverloadRecovered        bool              `json:"overload_recovered"`
	Sequence                 int64             `json:"sequence"`
	AirQuality               float64           `json:"air_quality"`
	LastDecision             string            `json:"last_decision"`
	LastAction               string            `json:"last_action"`
	LastReason               string            `json:"last_reason"`
	HomeStarted              bool              `json:"home_started"`
	VentilationEnabled       bool              `json:"ventilation_enabled"`
	SensorLossDetected       bool              `json:"sensor_loss_detected"`
	SafeStateSelected        bool              `json:"safe_state_selected"`
	SensorRecovered          bool              `json:"sensor_recovered"`
	Rooms                    []roomState       `json:"rooms"`
	Timeline                 []laboratoryEvent `json:"timeline"`
	NarrativeConnected       bool              `json:"narrative_connected"`
	NarrativeBuildID         string            `json:"narrative_build_id"`
	NarrativeBundleID        string            `json:"narrative_bundle_id"`
	NarrativeEventID         int64             `json:"narrative_event_id"`
	NarrativeDepth           int64             `json:"narrative_depth"`
	NarrativeTasks           []string          `json:"narrative_tasks"`
}

var laboratory = struct {
	sync.RWMutex
	state                laboratoryState
	sensorOverride       *bool
	eventCounter         int64
	recoveryGeneration   int64
	overloadGeneration   int64
	lastNarrativeRefresh time.Time
}{state: initialState()}

type persistentLaboratoryState struct {
	SchemaVersion  string          `json:"schema_version"`
	State          laboratoryState `json:"state"`
	SensorOverride *bool           `json:"sensor_override"`
	EventCounter   int64           `json:"event_counter"`
}

var narrativeClient = &http.Client{Timeout: 800 * time.Millisecond}

func initialState() laboratoryState {
	return laboratoryState{
		Scenario:                 "air-quality-and-sensor-loss",
		ExecutionMode:            "simulation",
		HardwareConnected:        false,
		Stage:                    "poznaj",
		SensorOnline:             true,
		AirQualityThreshold:      1000,
		FailurePolicy:            "keep-ventilation-safe",
		RecoveryMode:             "manual-approval",
		RecoveryDelay:            2000,
		EvidenceLimit:            18,
		RecoveryRequiredEvidence: 2,
	}
}

func evidencePath() string {
	directory := strings.TrimSpace(os.Getenv("JUNEXIS_LAB_STATE_DIR"))
	if directory == "" {
		return ""
	}
	return directory + string(os.PathSeparator) + "smart-home-state.json"
}

func persistLocked() {
	path := evidencePath()
	if path == "" {
		laboratory.state.EvidencePersistent = false
		laboratory.state.EvidencePath = ""
		return
	}
	laboratory.state.EvidencePersistent = true
	laboratory.state.EvidencePath = path
	snapshot := persistentLaboratoryState{
		SchemaVersion: "junexis.laboratory-state/v1", State: laboratory.state,
		SensorOverride: laboratory.sensorOverride, EventCounter: laboratory.eventCounter,
	}
	payload, err := json.MarshalIndent(snapshot, "", "  ")
	if err != nil {
		laboratory.state.EvidencePersistent = false
		return
	}
	directory := path[:strings.LastIndex(path, string(os.PathSeparator))]
	if os.MkdirAll(directory, 0o755) != nil {
		laboratory.state.EvidencePersistent = false
		return
	}
	temporary := path + ".new"
	if os.WriteFile(temporary, payload, 0o600) != nil {
		laboratory.state.EvidencePersistent = false
		return
	}
	_ = os.Remove(path)
	if os.Rename(temporary, path) != nil {
		laboratory.state.EvidencePersistent = false
		_ = os.Remove(temporary)
	}
}

func restorePersistentState() {
	path := evidencePath()
	if path == "" {
		return
	}
	laboratory.Lock()
	defer laboratory.Unlock()
	payload, err := os.ReadFile(path)
	if err == nil {
		var snapshot persistentLaboratoryState
		if json.Unmarshal(payload, &snapshot) == nil && snapshot.SchemaVersion == "junexis.laboratory-state/v1" {
			laboratory.state = snapshot.State
			// Starszy zapis stanu nie zawierał jawnej granicy wykonania.
			// Bieżący demonstrator zawsze działa jako symulacja i nie może po
			// odtworzeniu udawać połączenia z urządzeniem.
			laboratory.state.ExecutionMode = "simulation"
			laboratory.state.HardwareConnected = false
			laboratory.sensorOverride = snapshot.SensorOverride
			laboratory.eventCounter = snapshot.EventCounter
			laboratory.state.RestoredAfterRestart = true
			laboratory.state.RestoredEventCount = int64(len(snapshot.State.Timeline))
			laboratory.state.NarrativeConnected = false
			laboratory.state.NarrativeEventID = 0
			laboratory.state.NarrativeDepth = 0
			laboratory.state.NarrativeTasks = nil
			if laboratory.state.OverloadActive {
				laboratory.state.OverloadActive = false
				laboratory.state.OverloadRecovered = true
				appendEventLocked(laboratoryEvent{
					EventID: "overload-interrupted", Kind: "recovery", Title: "Restart przerwał opróżnianie bufora",
					Detail: "Nietrwała kolejka została bezpiecznie zamknięta; zachowano liczniki przyjęcia i odrzucenia.",
					Cause:  "process-restart", Effect: "transient-buffer-closed", Sequence: laboratory.state.Sequence,
				})
			}
			appendEventLocked(laboratoryEvent{
				EventID: "process-restarted", Kind: "recovery", Title: "Proces odtworzył stan laboratorium",
				Detail: "Konfiguracja operacyjna i zachowane dowody zostały wczytane z trwałego magazynu.",
				Cause:  "durable-state-loaded", Effect: "operational-context-restored", Sequence: laboratory.state.Sequence,
			})
			if laboratory.state.FaultForced && laboratory.state.RecoveryMode == "automatic" {
				laboratory.recoveryGeneration++
				scheduleAutomaticRecoveryLocked()
			}
		}
	}
	persistLocked()
}

func decode(value any, target any) {
	payload, err := json.Marshal(value)
	if err == nil {
		_ = json.Unmarshal(payload, target)
	}
}

func HomeLab_resolve_sensor(defaultOnline bool) bool {
	laboratory.RLock()
	defer laboratory.RUnlock()
	if laboratory.sensorOverride != nil {
		return *laboratory.sensorOverride
	}
	return defaultOnline
}

func HomeLab_air_quality_threshold() float64 {
	laboratory.RLock()
	defer laboratory.RUnlock()
	return laboratory.state.AirQualityThreshold
}

func HomeLab_apply_communication_delay() {
	laboratory.RLock()
	delay := laboratory.state.CommunicationDelay
	laboratory.RUnlock()
	if delay > 0 {
		time.Sleep(time.Duration(delay) * time.Millisecond)
	}
}

func HomeLab_failure_action() string {
	laboratory.RLock()
	defer laboratory.RUnlock()
	return laboratory.state.FailurePolicy
}

func HomeLab_record_reading(value any) {
	var reading sensorReading
	decode(value, &reading)

	laboratory.Lock()
	defer laboratory.Unlock()
	roomIndex := -1
	wasOnline := true
	for index := range laboratory.state.Rooms {
		if laboratory.state.Rooms[index].Room == reading.Room {
			roomIndex = index
			wasOnline = laboratory.state.Rooms[index].Online
			break
		}
	}
	currentRoom := roomState{
		Room: reading.Room, Celsius: reading.Celsius, Humidity: reading.Humidity,
		AirQuality: reading.AirQuality, Motion: reading.Motion, Online: reading.Online,
		Sequence: reading.Sequence,
	}
	if roomIndex >= 0 {
		laboratory.state.Rooms[roomIndex] = currentRoom
	} else {
		laboratory.state.Rooms = append(laboratory.state.Rooms, currentRoom)
		appendEventLocked(laboratoryEvent{
			EventID: "room-discovered-" + reading.Room, Kind: "observation", Room: reading.Room,
			Title: "Pomieszczenie dołączyło do topologii", Detail: "Pierwszy poprawny pomiar dotarł do serwera.",
			Cause: "sensor-reading", Effect: "room-visible", Sequence: reading.Sequence,
		})
	}
	laboratory.state.HomeStarted = len(laboratory.state.Rooms) >= 3
	laboratory.state.Sequence = reading.Sequence
	if reading.Room == "living-room" {
		laboratory.state.SensorOnline = reading.Online
		laboratory.state.AirQuality = reading.AirQuality
	}
	if reading.Room == "living-room" && !reading.Online {
		laboratory.state.SensorLossDetected = true
		if wasOnline {
			appendEventLocked(laboratoryEvent{
				EventID: "sensor-loss-" + formatSequence(reading.Sequence), Kind: "failure", Room: reading.Room,
				Title: "Czujnik salonu przestał odpowiadać", Detail: "System oznaczył bieżący pomiar jako niedostępny.",
				Cause: "sensor-offline", Effect: "limited-mode", Sequence: reading.Sequence,
			})
		}
	}
	if reading.Room == "living-room" && reading.Online && laboratory.state.RecoveryPending && !laboratory.state.FaultForced {
		laboratory.state.RecoveryFreshEvidence++
		appendEventLocked(laboratoryEvent{
			EventID: "recovery-evidence-" + formatSequence(laboratory.state.RecoveryFreshEvidence), Kind: "recovery", Room: reading.Room,
			Title: "Przyjęto świeży dowód po recovery", Detail: fmt.Sprintf("Pomiar %d/%d po odblokowaniu źródła.", laboratory.state.RecoveryFreshEvidence, laboratory.state.RecoveryRequiredEvidence),
			Cause: "fresh-reading-after-recovery", Effect: "recovery-validation-progress", Sequence: reading.Sequence,
		})
		if laboratory.state.RecoveryFreshEvidence >= laboratory.state.RecoveryRequiredEvidence {
			laboratory.state.RecoveryPending = false
			laboratory.state.SensorRecovered = true
			appendEventLocked(laboratoryEvent{
				EventID: "recovery-validated-" + formatSequence(reading.Sequence), Kind: "recovery", Room: reading.Room,
				Title: "Recovery potwierdzone świeżymi danymi", Detail: "Dwa kolejne pomiary zakończyły oczekiwanie recovery.",
				Cause: "fresh-evidence-complete", Effect: "normal-mode", Sequence: reading.Sequence,
			})
		}
	} else if reading.Room == "living-room" && reading.Online && !wasOnline {
		laboratory.state.SensorRecovered = true
		appendEventLocked(laboratoryEvent{
			EventID: "sensor-recovery-" + formatSequence(reading.Sequence), Kind: "recovery", Room: reading.Room,
			Title: "Czujnik salonu wrócił", Detail: "Świeży pomiar ponownie uczestniczy w podejmowaniu decyzji.",
			Cause: "fresh-reading", Effect: "normal-mode", Sequence: reading.Sequence,
		})
	}
	persistLocked()
}

func HomeLab_record_decision(value any) {
	var decision homeDecision
	decode(value, &decision)

	laboratory.Lock()
	defer laboratory.Unlock()
	laboratory.state.LastDecision = decision.DecisionID
	laboratory.state.LastAction = decision.Action
	laboratory.state.LastReason = decision.Reason
	laboratory.state.Sequence = decision.Sequence
	if decision.Action == "enable-ventilation" {
		laboratory.state.VentilationEnabled = true
	}
	if decision.DecisionID == "sensor-unavailable" &&
		(decision.Action == "keep-ventilation-safe" || decision.Action == "pause-automation") {
		laboratory.state.SafeStateSelected = true
	}
	appendEventLocked(laboratoryEvent{
		EventID: decision.DecisionID + "-" + decision.Room + "-" + formatSequence(decision.Sequence),
		Kind:    "decision", Room: decision.Room, Title: decisionTitle(decision.Action), Detail: decision.Reason,
		Cause: decision.Rule, Effect: decision.Action, Sequence: decision.Sequence,
	})
	persistLocked()
}

func formatSequence(sequence int64) string {
	return strconv.FormatInt(sequence, 10)
}

func decisionTitle(action string) string {
	switch action {
	case "enable-ventilation":
		return "Dom włączył wentylację"
	case "keep-ventilation-safe":
		return "Dom wybrał stan bezpieczny"
	case "pause-automation":
		return "Dom wstrzymał automatykę"
	default:
		return "Dom kontynuuje obserwację"
	}
}

func appendEventLocked(event laboratoryEvent) {
	laboratory.eventCounter++
	event.EventID += "-" + strconv.FormatInt(laboratory.eventCounter, 10)
	event.OccurredAt = time.Now().UTC().Format(time.RFC3339)
	laboratory.state.Timeline = append(laboratory.state.Timeline, event)
	limit := int(laboratory.state.EvidenceLimit)
	if limit < 1 {
		limit = 18
	}
	if len(laboratory.state.Timeline) > limit {
		laboratory.state.Timeline = append([]laboratoryEvent(nil), laboratory.state.Timeline[len(laboratory.state.Timeline)-limit:]...)
	}
}

func HomeLab_current() any {
	refreshNarrativeEvidence()
	laboratory.RLock()
	defer laboratory.RUnlock()
	snapshot := laboratory.state
	snapshot.Rooms = append([]roomState(nil), laboratory.state.Rooms...)
	snapshot.Timeline = append([]laboratoryEvent(nil), laboratory.state.Timeline...)
	snapshot.NarrativeTasks = append([]string(nil), laboratory.state.NarrativeTasks...)
	return snapshot
}

func refreshNarrativeEvidence() {
	collectorURL := strings.TrimRight(os.Getenv("JUNEXIS_TRACE_URL"), "/")
	buildID := os.Getenv("JUNEXIS_BUILD_ID")
	bundleID := os.Getenv("JUNEXIS_BUNDLE_ID")
	if collectorURL == "" || buildID == "" || bundleID == "" {
		return
	}

	laboratory.Lock()
	if time.Since(laboratory.lastNarrativeRefresh) < 750*time.Millisecond {
		laboratory.Unlock()
		return
	}
	laboratory.lastNarrativeRefresh = time.Now()
	laboratory.state.NarrativeBuildID = buildID
	laboratory.state.NarrativeBundleID = bundleID
	laboratory.Unlock()

	latestResponse, err := narrativeClient.Get(collectorURL + "/events?limit=500")
	if err != nil {
		setNarrativeDisconnected()
		return
	}
	defer latestResponse.Body.Close()
	if latestResponse.StatusCode != http.StatusOK {
		setNarrativeDisconnected()
		return
	}
	var events []traceEvent
	if json.NewDecoder(latestResponse.Body).Decode(&events) != nil {
		setNarrativeDisconnected()
		return
	}
	var latest traceEvent
	for _, event := range events {
		if event.Function == "HomeObserver" &&
			event.Attributes["type"] == "HomeDecision" &&
			event.Attributes["build_id"] == buildID &&
			event.Attributes["bundle_id"] == bundleID &&
			event.Attributes["narrative_id"] == "Smart Home Laboratory" &&
			event.EventID > latest.EventID {
			latest = event
		}
	}
	if latest.EventID == 0 {
		setNarrativeDisconnected()
		return
	}

	whyResponse, err := narrativeClient.Get(collectorURL + "/events/" + strconv.FormatInt(latest.EventID, 10) + "/why")
	if err != nil {
		setNarrativeDisconnected()
		return
	}
	defer whyResponse.Body.Close()
	if whyResponse.StatusCode != http.StatusOK {
		setNarrativeDisconnected()
		return
	}
	var why whyResult
	if json.NewDecoder(whyResponse.Body).Decode(&why) != nil {
		setNarrativeDisconnected()
		return
	}

	seen := map[string]bool{}
	tasks := make([]string, 0, 3)
	for _, event := range why.CausalChain {
		if event.Function != "" && !seen[event.Function] {
			seen[event.Function] = true
			tasks = append(tasks, event.Function)
		}
	}

	laboratory.Lock()
	laboratory.state.NarrativeConnected = true
	laboratory.state.NarrativeEventID = latest.EventID
	laboratory.state.NarrativeDepth = why.Depth
	laboratory.state.NarrativeTasks = tasks
	laboratory.Unlock()
}

func setNarrativeDisconnected() {
	laboratory.Lock()
	laboratory.state.NarrativeConnected = false
	laboratory.state.NarrativeEventID = 0
	laboratory.state.NarrativeDepth = 0
	laboratory.state.NarrativeTasks = nil
	laboratory.Unlock()
}

func HomeLab_set_fault(value any) any {
	var command faultCommand
	decode(value, &command)
	laboratory.Lock()
	laboratory.recoveryGeneration++
	override := !command.Active
	laboratory.sensorOverride = &override
	laboratory.state.FaultForced = command.Active
	laboratory.state.RecoveryPending = command.Active
	if command.Active {
		laboratory.state.RecoveryFreshEvidence = 0
	}
	appendEventLocked(laboratoryEvent{
		EventID: "fault-control", Kind: "control", Room: "living-room",
		Title: "Użytkownik wymusił awarię", Detail: "Symulator przestanie zgłaszać dostępność czujnika salonu.",
		Cause: "laboratory-control", Effect: "sensor-offline", Sequence: laboratory.state.Sequence,
	})
	if command.Active && laboratory.state.RecoveryMode == "automatic" {
		scheduleAutomaticRecoveryLocked()
	}
	persistLocked()
	laboratory.Unlock()
	return HomeLab_current()
}

func HomeLab_apply_control(value any) any {
	var command controlCommand
	decode(value, &command)
	command.AirQualityThreshold, command.CommunicationDelay, command.FailurePolicy = validateExperimentValues(
		command.AirQualityThreshold, command.CommunicationDelay, command.FailurePolicy)
	if command.RecoveryMode != "automatic" && command.RecoveryMode != "manual-approval" {
		command.RecoveryMode = "manual-approval"
	}
	if command.RecoveryDelay < 250 || command.RecoveryDelay > 10000 {
		command.RecoveryDelay = 2000
	}
	if command.EvidenceLimit < 6 || command.EvidenceLimit > 60 {
		command.EvidenceLimit = 18
	}

	laboratory.Lock()
	laboratory.recoveryGeneration++
	laboratory.state.Stage = "przejmij-kontrole"
	laboratory.state.AirQualityThreshold = command.AirQualityThreshold
	laboratory.state.CommunicationDelay = command.CommunicationDelay
	laboratory.state.FailurePolicy = command.FailurePolicy
	laboratory.state.RecoveryMode = command.RecoveryMode
	laboratory.state.RecoveryDelay = command.RecoveryDelay
	laboratory.state.EvidenceLimit = command.EvidenceLimit
	laboratory.state.ControlRevision++
	appendEventLocked(laboratoryEvent{
		EventID: "expert-control-applied", Kind: "control", Title: "Ekspert przejął konfigurację",
		Detail: fmt.Sprintf("Recovery %s po %d ms; limit dowodów %d.", command.RecoveryMode, command.RecoveryDelay, command.EvidenceLimit),
		Cause:  "explicit-effective-configuration", Effect: "expert-policy-active", Sequence: laboratory.state.Sequence,
	})
	if laboratory.state.FaultForced && command.RecoveryMode == "automatic" {
		scheduleAutomaticRecoveryLocked()
	}
	persistLocked()
	laboratory.Unlock()
	return HomeLab_current()
}

func HomeLab_recover(value any) any {
	var command recoveryCommand
	decode(value, &command)
	laboratory.Lock()
	if !laboratory.state.FaultForced {
		laboratory.Unlock()
		return HomeLab_current()
	}
	if !command.Approved {
		appendEventLocked(laboratoryEvent{
			EventID: "recovery-rejected", Kind: "recovery", Room: "living-room",
			Title: "Recovery nie zostało zatwierdzone", Detail: "System pozostaje w stanie ograniczonym.",
			Cause: "operator-rejected", Effect: "fault-remains-active", Sequence: laboratory.state.Sequence,
		})
		persistLocked()
		laboratory.Unlock()
		return HomeLab_current()
	}
	laboratory.recoveryGeneration++
	completeRecoveryLocked("operator-approved")
	persistLocked()
	laboratory.Unlock()
	return HomeLab_current()
}

func scheduleAutomaticRecoveryLocked() {
	generation := laboratory.recoveryGeneration
	delay := laboratory.state.RecoveryDelay
	go func() {
		time.Sleep(time.Duration(delay) * time.Millisecond)
		laboratory.Lock()
		defer laboratory.Unlock()
		if laboratory.recoveryGeneration != generation || !laboratory.state.FaultForced || laboratory.state.RecoveryMode != "automatic" {
			return
		}
		completeRecoveryLocked("automatic-recovery-policy")
		persistLocked()
	}()
}

func completeRecoveryLocked(cause string) {
	laboratory.sensorOverride = nil
	laboratory.state.FaultForced = false
	laboratory.state.RecoveryPending = true
	laboratory.state.RecoveryFreshEvidence = 0
	appendEventLocked(laboratoryEvent{
		EventID: "recovery-completed", Kind: "recovery", Room: "living-room",
		Title: "Źródło zostało odblokowane", Detail: "Usunięto wymuszenie awarii; recovery czeka na dwa świeże pomiary.",
		Cause: cause, Effect: "sensor-override-cleared-awaiting-evidence", Sequence: laboratory.state.Sequence,
	})
}

func HomeLab_configure_experiment(value any) any {
	var command experimentCommand
	decode(value, &command)
	command.AirQualityThreshold, command.CommunicationDelay, command.FailurePolicy = validateExperimentValues(
		command.AirQualityThreshold, command.CommunicationDelay, command.FailurePolicy)

	laboratory.Lock()
	laboratory.state.Stage = "eksperymentuj"
	laboratory.state.AirQualityThreshold = command.AirQualityThreshold
	laboratory.state.CommunicationDelay = command.CommunicationDelay
	laboratory.state.FailurePolicy = command.FailurePolicy
	laboratory.state.ExperimentRevision++
	appendEventLocked(laboratoryEvent{
		EventID: "experiment-configured", Kind: "experiment", Title: "Zmieniono warunki eksperymentu",
		Detail: fmt.Sprintf("Próg %.0f ppm, opóźnienie %d ms, polityka %s.", command.AirQualityThreshold, command.CommunicationDelay, command.FailurePolicy),
		Cause:  "learner-hypothesis", Effect: "runtime-policy-updated", Sequence: laboratory.state.Sequence,
	})
	persistLocked()
	laboratory.Unlock()
	return HomeLab_current()
}

func HomeLab_run_overload(value any) any {
	var command overloadCommand
	decode(value, &command)
	if command.BurstSize < 1 || command.BurstSize > 200 {
		command.BurstSize = 24
	}
	if command.BufferLimit < 1 || command.BufferLimit > 100 {
		command.BufferLimit = 8
	}
	if command.ConsumerDelay < 1 || command.ConsumerDelay > 500 {
		command.ConsumerDelay = 40
	}

	queue := make(chan int64, int(command.BufferLimit))
	accepted := int64(0)
	dropped := int64(0)
	for sequence := int64(1); sequence <= command.BurstSize; sequence++ {
		select {
		case queue <- sequence:
			accepted++
		default:
			dropped++
		}
	}
	close(queue)

	laboratory.Lock()
	laboratory.overloadGeneration++
	generation := laboratory.overloadGeneration
	laboratory.state.Stage = "przejmij-kontrole"
	laboratory.state.OverloadActive = true
	laboratory.state.OverloadBurstSize = command.BurstSize
	laboratory.state.OverloadBufferLimit = command.BufferLimit
	laboratory.state.OverloadDelay = command.ConsumerDelay
	laboratory.state.OverloadAccepted = accepted
	laboratory.state.OverloadDropped = dropped
	laboratory.state.OverloadProcessed = 0
	laboratory.state.OverloadRecovered = false
	appendEventLocked(laboratoryEvent{
		EventID: "bounded-overload", Kind: "failure", Title: "Ograniczony bufor przyjął serię zdarzeń",
		Detail: fmt.Sprintf("Seria %d: przyjęto %d, odrzucono %d przy limicie %d.", command.BurstSize, accepted, dropped, command.BufferLimit),
		Cause:  "producer-faster-than-consumer", Effect: "bounded-backpressure", Sequence: laboratory.state.Sequence,
	})
	persistLocked()
	laboratory.Unlock()

	go func() {
		processed := int64(0)
		for range queue {
			time.Sleep(time.Duration(command.ConsumerDelay) * time.Millisecond)
			processed++
		}
		laboratory.Lock()
		defer laboratory.Unlock()
		if laboratory.overloadGeneration != generation {
			return
		}
		laboratory.state.OverloadActive = false
		laboratory.state.OverloadProcessed = processed
		laboratory.state.OverloadRecovered = true
		appendEventLocked(laboratoryEvent{
			EventID: "overload-drained", Kind: "recovery", Title: "Konsument opróżnił ograniczony bufor",
			Detail: fmt.Sprintf("Przetworzono %d przyjętych zdarzeń; system wrócił do stanu stabilnego.", processed),
			Cause:  "bounded-queue-drained", Effect: "stable-throughput-restored", Sequence: laboratory.state.Sequence,
		})
		persistLocked()
	}()
	return HomeLab_current()
}

func validateExperimentValues(threshold float64, delay int64, failurePolicy string) (float64, int64, string) {
	if threshold < 600 || threshold > 2000 {
		threshold = 1000
	}
	if delay < 0 || delay > 3000 {
		delay = 0
	}
	if failurePolicy != "keep-ventilation-safe" && failurePolicy != "pause-automation" {
		failurePolicy = "keep-ventilation-safe"
	}
	return threshold, delay, failurePolicy
}

func HomeLab_reset() any {
	laboratory.Lock()
	laboratory.recoveryGeneration++
	laboratory.overloadGeneration++
	laboratory.state = initialState()
	laboratory.sensorOverride = nil
	appendEventLocked(laboratoryEvent{
		EventID: "laboratory-reset", Kind: "control", Title: "Laboratorium zostało zresetowane",
		Detail: "Usunięto wymuszenia i przywrócono znany stan początkowy.",
		Cause:  "user-reset", Effect: "initial-state", Sequence: 0,
	})
	persistLocked()
	laboratory.Unlock()
	return HomeLab_current()
}

func init() {
	restorePersistentState()
	http.HandleFunc("GET /laboratory/", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		_, _ = w.Write([]byte(laboratoryPanel))
	})
}

const laboratoryPanel = `<!doctype html>
<html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Laboratorium Junexis — Inteligentny dom</title>
<style>
:root{color-scheme:dark;--bg:#07120f;--panel:#0d211b;--line:#214a3d;--mint:#71efbd;--warm:#ffca72;--red:#ff7f72;--muted:#a9c0b8}*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at 15% 0,#153d30 0,transparent 38%),var(--bg);color:#f4fff9;font:16px/1.5 system-ui,sans-serif}.shell{max-width:1180px;margin:auto;padding:48px 24px}header{display:flex;justify-content:space-between;gap:24px;align-items:end;margin-bottom:28px}.eyebrow{color:var(--mint);font-weight:700;letter-spacing:.12em;text-transform:uppercase}h1{font-size:clamp(2rem,5vw,4rem);line-height:1;margin:.3rem 0}.stage{color:var(--warm)}.grid{display:grid;grid-template-columns:1.3fr 1fr;gap:18px}.card{background:color-mix(in srgb,var(--panel) 92%,transparent);border:1px solid var(--line);border-radius:18px;padding:22px}.metric{font-size:2.6rem;font-weight:750}.muted{color:var(--muted)}.status{display:inline-flex;gap:8px;align-items:center}.dot{width:10px;height:10px;border-radius:50%;background:var(--mint);box-shadow:0 0 18px var(--mint)}.fault .dot{background:var(--red);box-shadow:0 0 18px var(--red)}.rooms{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin:18px 0}.room{padding:14px;border-radius:14px;background:#091914;border:1px solid var(--line)}.room strong{display:block;font-size:1.05rem}.room .values{display:flex;gap:12px;margin-top:8px;color:var(--muted)}.checkpoints{display:grid;gap:10px;margin-top:16px}.checkpoint{display:flex;justify-content:space-between;padding:10px 12px;border-radius:10px;background:#091914}.checkpoint.done{outline:1px solid #3b8e70;color:var(--mint)}.timeline{grid-column:1/-1}.narrative{display:flex;flex-wrap:wrap;gap:10px 18px;align-items:center;padding:14px;margin:14px 0;background:#091914;border:1px solid var(--line);border-radius:12px}.narrative strong{color:var(--mint)}.narrative.offline strong{color:var(--muted)}.events{display:grid;gap:0;margin-top:12px}.event{display:grid;grid-template-columns:92px 1fr;gap:16px;padding:14px 0;border-top:1px solid var(--line)}.event-kind{color:var(--warm);font-size:.78rem;font-weight:800;text-transform:uppercase}.chain{color:var(--muted);font-size:.9rem;margin-top:4px}.chain b{color:var(--mint)}button{border:0;border-radius:10px;padding:12px 16px;font-weight:750;cursor:pointer;margin:8px 8px 0 0}button.primary{background:var(--red);color:#240b08}button.secondary{background:#d7eee5;color:#092018}@media(max-width:760px){header{align-items:start;flex-direction:column}.grid{grid-template-columns:1fr}.rooms{grid-template-columns:1fr}.event{grid-template-columns:1fr}}
</style></head><body><main class="shell"><header><div><div class="eyebrow">Laboratorium Junexis</div><h1>Dom, który zauważa i reaguje</h1><div class="muted">Trzy pomieszczenia · prawdziwy stan programu · etap <span class="stage">Poznaj</span></div></div><div id="connection" class="status"><span class="dot"></span>połączono</div></header><div id="rooms" class="rooms"></div><section class="grid"><article class="card"><div class="muted">Jakość powietrza w salonie</div><div class="metric"><span id="air">—</span> ppm</div><p id="decision" class="muted">Oczekiwanie na pierwszą decyzję…</p><h2 id="action">—</h2><button class="primary" id="fault">Zasymuluj awarię czujnika</button><button class="secondary" id="reset">Przywróć automatykę</button></article><aside class="card"><div class="muted">Punkty kontrolne</div><div id="sensor" class="status"><span class="dot"></span><strong>czujnik salonu online</strong></div><div class="checkpoints" id="checkpoints"></div></aside><article class="card timeline"><div class="eyebrow">Oś przyczyn</div><h2>Co się wydarzyło i dlaczego</h2><div id="narrative" class="narrative offline"><strong>Narrative: tryb fail-open</strong><span>Uruchomienie bez TraceCollectora nie blokuje domu.</span></div><div id="events" class="events"></div></article></section></main><script>
const labels={home_started:'Dom uruchomiony',ventilation_enabled:'Wentylacja włączona',sensor_loss_detected:'Utrata czujnika wykryta',safe_state_selected:'Wybrano stan bezpieczny',sensor_recovered:'Czujnik odzyskany'};
const roomNames={kitchen:'Kuchnia',bedroom:'Sypialnia','living-room':'Salon'};
const actionNames={observe:'Obserwacja','enable-ventilation':'Wentylacja włączona','keep-ventilation-safe':'Bezpieczna wentylacja'};
actionNames['pause-automation']='Automatyka wstrzymana';
async function command(path,body){await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});await refresh()}
async function refresh(){try{const r=await fetch('/api/laboratory/state',{cache:'no-store'});if(!r.ok)throw Error(r.status);const s=await r.json();air.textContent=Math.round(s.air_quality);action.textContent=actionNames[s.last_action]||'Obserwacja';decision.textContent=s.last_reason||'Oczekiwanie na pierwszą decyzję…';sensor.className='status '+(s.sensor_online?'':'fault');sensor.innerHTML='<span class="dot"></span><strong>czujnik salonu '+(s.sensor_online?'online':'— awaria')+'</strong>';fault.textContent=s.fault_forced?'Awaria jest aktywna':'Zasymuluj awarię czujnika';rooms.innerHTML=(s.rooms||[]).map(x=>'<div class="room '+(x.online?'':'fault')+'"><strong><span class="dot"></span> '+(roomNames[x.room]||x.room)+'</strong><div class="values"><span>'+x.celsius.toFixed(1)+'°C</span><span>'+Math.round(x.humidity)+'%</span><span>'+Math.round(x.air_quality)+' ppm</span></div></div>').join('');checkpoints.innerHTML=Object.entries(labels).map(([k,v])=>'<div class="checkpoint '+(s[k]?'done':'')+'"><span>'+v+'</span><strong>'+(s[k]?'✓':'·')+'</strong></div>').join('');narrative.className='narrative '+(s.narrative_connected?'':'offline');narrative.innerHTML=s.narrative_connected?'<strong>Narrative połączone</strong><span>dowód #'+s.narrative_event_id+'</span><span>głębokość '+s.narrative_depth+'</span><span>'+s.narrative_tasks.join(' → ')+'</span><span>build '+s.narrative_build_id+' · bundle '+s.narrative_bundle_id+'</span>':'<strong>Narrative: tryb fail-open</strong><span>Uruchomienie bez TraceCollectora nie blokuje domu.</span>';events.innerHTML=[...(s.timeline||[])].reverse().map(e=>'<div class="event"><div class="event-kind">'+e.kind+'</div><div><strong>'+e.title+'</strong><div>'+e.detail+'</div><div class="chain">przyczyna: <b>'+e.cause+'</b> → skutek: <b>'+e.effect+'</b> · '+(roomNames[e.room]||e.room||'system')+'</div></div></div>').join('');connection.className='status';connection.innerHTML='<span class="dot"></span>połączono'}catch(e){connection.className='status fault';connection.innerHTML='<span class="dot"></span>brak połączenia'}}
document.head.insertAdjacentHTML('beforeend','<style>.experiment-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}.field{display:grid;gap:6px}.field input,.field select{width:100%;border:1px solid var(--line);border-radius:9px;background:#07120f;color:#f4fff9;padding:10px}.hypothesis{margin:14px 0;padding:12px;border-left:3px solid var(--warm);background:#091914}.result{color:var(--mint);margin-left:10px}@media(max-width:760px){.experiment-grid{grid-template-columns:1fr}}</style>');
const experiment=document.createElement('article');experiment.className='card timeline';experiment.innerHTML='<div class="eyebrow">Eksperymentuj</div><h2>Postaw hipotezę i zmień zachowanie domu</h2><p class="hypothesis">Przewidź: kiedy dom włączy wentylację i co zrobi, gdy wiadomość lub czujnik przestaną być dostępne?</p><div class="experiment-grid"><label class="field">Próg jakości powietrza (ppm)<input id="experiment-threshold" type="number" min="600" max="2000" step="50" value="1000"></label><label class="field">Opóźnienie komunikacji<select id="experiment-delay"><option value="0">brak</option><option value="500">500 ms</option><option value="1500">1500 ms</option><option value="3000">3000 ms</option></select></label><label class="field">Polityka utraty czujnika<select id="experiment-policy"><option value="keep-ventilation-safe">utrzymuj bezpieczną wentylację</option><option value="pause-automation">wstrzymaj automatykę</option></select></label></div><button class="secondary" id="experiment-apply">Uruchom eksperyment</button><span id="experiment-result" class="result"></span>';
document.querySelector('.timeline').before(experiment);
const experimentThreshold=document.getElementById('experiment-threshold'),experimentDelay=document.getElementById('experiment-delay'),experimentPolicy=document.getElementById('experiment-policy'),experimentResult=document.getElementById('experiment-result');
const refreshPoznaj=refresh;refresh=async function(){await refreshPoznaj();try{const r=await fetch('/api/laboratory/state',{cache:'no-store'});if(!r.ok)return;const s=await r.json();document.querySelector('.stage').textContent=s.stage==='eksperymentuj'?'Eksperymentuj':'Poznaj';if(document.activeElement!==experimentThreshold)experimentThreshold.value=s.air_quality_threshold;if(document.activeElement!==experimentDelay)experimentDelay.value=String(s.communication_delay_ms);if(document.activeElement!==experimentPolicy)experimentPolicy.value=s.failure_policy;experimentResult.textContent=s.experiment_revision?'Aktywny eksperyment #'+s.experiment_revision:''}catch(e){}}
document.getElementById('experiment-apply').onclick=()=>command('/api/laboratory/experiment',{air_quality_threshold:Number(experimentThreshold.value),communication_delay_ms:Number(experimentDelay.value),failure_policy:experimentPolicy.value});
document.head.insertAdjacentHTML('beforeend','<style>.effective{white-space:pre-wrap;background:#06100d;border:1px solid var(--line);border-radius:10px;padding:14px;color:var(--mint);overflow:auto}.control-actions{display:flex;flex-wrap:wrap;align-items:center;gap:6px}</style>');
const control=document.createElement('article');control.className='card timeline';control.innerHTML='<div class="eyebrow">Przejmij kontrolę</div><h2>Jawna polityka recovery i dowodów</h2><p class="muted">Te ustawienia zmieniają kontrakt operacyjny domu. Zapisz powód decyzji i przygotuj drogę powrotu.</p><div class="experiment-grid"><label class="field">Tryb recovery<select id="control-recovery-mode"><option value="manual-approval">ręczne zatwierdzenie</option><option value="automatic">automatyczne</option></select></label><label class="field">Opóźnienie recovery (ms)<input id="control-recovery-delay" type="number" min="250" max="10000" step="250" value="2000"></label><label class="field">Limit zdarzeń dowodowych<input id="control-evidence-limit" type="number" min="6" max="60" value="18"></label></div><div class="control-actions"><button class="secondary" id="control-apply">Zastosuj pełną konfigurację</button><button class="secondary" id="control-approve">Zatwierdź recovery</button><button class="primary" id="control-reject">Odrzuć recovery</button><span id="control-result" class="result"></span></div><h3>Próba przeciążenia</h3><div class="experiment-grid"><label class="field">Rozmiar serii<input id="overload-burst" type="number" min="1" max="200" value="24"></label><label class="field">Limit bufora<input id="overload-buffer" type="number" min="1" max="100" value="8"></label><label class="field">Czas konsumenta (ms)<input id="overload-delay" type="number" min="1" max="500" value="40"></label></div><div class="control-actions"><button class="primary" id="overload-run">Wywołaj kontrolowane przeciążenie</button><span id="overload-result" class="result"></span></div><h3>Efektywna konfiguracja i trwałość</h3><pre id="effective-config" class="effective">Oczekiwanie na stan…</pre>';
experiment.after(control);
const recoveryMode=document.getElementById('control-recovery-mode'),recoveryDelay=document.getElementById('control-recovery-delay'),evidenceLimit=document.getElementById('control-evidence-limit'),controlResult=document.getElementById('control-result'),effectiveConfig=document.getElementById('effective-config'),overloadBurst=document.getElementById('overload-burst'),overloadBuffer=document.getElementById('overload-buffer'),overloadDelay=document.getElementById('overload-delay'),overloadResult=document.getElementById('overload-result');
const refreshEksperymentuj=refresh;refresh=async function(){await refreshEksperymentuj();try{const r=await fetch('/api/laboratory/state',{cache:'no-store'});if(!r.ok)return;const s=await r.json();if(s.stage==='przejmij-kontrole')document.querySelector('.stage').textContent='Przejmij kontrolę';if(document.activeElement!==recoveryMode)recoveryMode.value=s.recovery_mode;if(document.activeElement!==recoveryDelay)recoveryDelay.value=s.recovery_delay_ms;if(document.activeElement!==evidenceLimit)evidenceLimit.value=s.evidence_limit;controlResult.textContent=s.recovery_pending?'Recovery oczekuje · świeże dowody '+s.recovery_fresh_evidence+'/'+s.recovery_required_evidence:(s.control_revision?'Konfiguracja ekspercka #'+s.control_revision:'');overloadResult.textContent=s.overload_active?'Bufor jest opróżniany…':(s.overload_burst_size?'Przyjęto '+s.overload_accepted+', odrzucono '+s.overload_dropped+', przetworzono '+s.overload_processed:'');effectiveConfig.textContent=JSON.stringify({air_quality_threshold:s.air_quality_threshold,communication_delay_ms:s.communication_delay_ms,failure_policy:s.failure_policy,recovery_mode:s.recovery_mode,recovery_delay_ms:s.recovery_delay_ms,recovery_fresh_evidence:s.recovery_fresh_evidence,recovery_required_evidence:s.recovery_required_evidence,evidence_limit:s.evidence_limit,evidence_persistent:s.evidence_persistent,restored_after_restart:s.restored_after_restart,restored_event_count:s.restored_event_count,evidence_path:s.evidence_path},null,2)}catch(e){}}
document.getElementById('control-apply').onclick=()=>command('/api/laboratory/control',{air_quality_threshold:Number(experimentThreshold.value),communication_delay_ms:Number(experimentDelay.value),failure_policy:experimentPolicy.value,recovery_mode:recoveryMode.value,recovery_delay_ms:Number(recoveryDelay.value),evidence_limit:Number(evidenceLimit.value)});
document.getElementById('overload-run').onclick=()=>command('/api/laboratory/overload',{burst_size:Number(overloadBurst.value),buffer_limit:Number(overloadBuffer.value),consumer_delay_ms:Number(overloadDelay.value)});
document.getElementById('control-approve').onclick=()=>command('/api/laboratory/recover',{approved:true});document.getElementById('control-reject').onclick=()=>command('/api/laboratory/recover',{approved:false});
const runtime=document.createElement('article');runtime.className='card timeline';runtime.innerHTML='<div class="eyebrow">Dom 10 · Trwałość procesu</div><h2>Co przetrwa restart, a co zostaje w pamięci?</h2><p class="muted">Konfiguracja, awaria i ograniczona oś są zapisywane atomowo. Elementy oczekujące w ograniczonym buforze są pracą nietrwałą: po restarcie system zamyka ich kontekst jawnie, zamiast udawać dokończenie.</p><pre id="runtime-contract" class="effective">Oczekiwanie na stan…</pre>';
document.querySelector('.grid').appendChild(runtime);const runtimeContract=document.getElementById('runtime-contract');const refreshControl=refresh;refresh=async function(){await refreshControl();try{const r=await fetch('/api/laboratory/state',{cache:'no-store'});if(!r.ok)return;const s=await r.json();runtimeContract.textContent=JSON.stringify({trwale:{konfiguracja:true,wymuszona_awaria:s.fault_forced,zdarzenia:s.timeline.length,magazyn_aktywny:s.evidence_persistent,odtworzono_po_restarcie:s.restored_after_restart},nietrwale:{bufor_w_pamieci:s.overload_active,przyjeto:s.overload_accepted,odrzucono:s.overload_dropped,przetworzono:s.overload_processed,jawnie_zamkniety:s.overload_recovered}},null,2)}catch(e){}}
fault.onclick=()=>command('/api/laboratory/fault',{active:true});reset.onclick=()=>command('/api/laboratory/reset');refresh();setInterval(refresh,750);
</script></body></html>`
