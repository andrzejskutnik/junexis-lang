package user

import (
	"encoding/json"
	"fmt"
	"log"
	"net"
	"net/http"
	"sort"
	"sync"
	"time"
)

const recoverySilenceTimeout = 4500 * time.Millisecond
const requiredFreshEvidence = 2

type recoverySignal struct {
	Kind string `json:"kind"`
	Room string `json:"room"`
	AirQuality float64 `json:"air_quality"`
	SourceStatus string `json:"source_status"`
	SilenceAge int `json:"silence_age_ms"`
	SilenceTimeout int `json:"silence_timeout_ms"`
	ObservedAt time.Time `json:"observed_at"`
}

type recoveryAssessment struct {
	RecoveryMode string `json:"recovery_mode"`
	RecoveryPhase string `json:"recovery_phase"`
	FreshEvidence int `json:"fresh_evidence"`
	RequiredEvidence int `json:"required_evidence"`
	Approved bool `json:"approved"`
	AllowControl bool `json:"allow_control"`
	RecoveryRevision int `json:"recovery_revision"`
}

type recoveryRoomState struct {
	Room string `json:"room"`
	AirQuality float64 `json:"air_quality"`
	Decision string `json:"decision"`
	RequestedEnabled bool `json:"requested_enabled"`
	ActualEnabled bool `json:"actual_enabled"`
	Status string `json:"status"`
	Cause string `json:"cause"`
	SourceStatus string `json:"source_status"`
	RecoveryMode string `json:"recovery_mode"`
	RecoveryPhase string `json:"recovery_phase"`
	FreshEvidence int `json:"fresh_evidence"`
	RequiredEvidence int `json:"required_evidence"`
	Approved bool `json:"approved"`
	RecoveryRevision int `json:"recovery_revision"`
	ConfirmedAt time.Time `json:"confirmed_at"`
}

type recoveryEvent struct {
	Step int `json:"step"`
	Title string `json:"title"`
	Evidence string `json:"evidence"`
	Effect string `json:"effect"`
	OccurredAt time.Time `json:"occurred_at"`
}

type recoveryResponse struct {
	SourceEnabled bool `json:"source_enabled"`
	RecoveryMode string `json:"recovery_mode"`
	RecoveryPhase string `json:"recovery_phase"`
	FreshEvidence int `json:"fresh_evidence"`
	RequiredEvidence int `json:"required_evidence"`
	Approved bool `json:"approved"`
	RecoveryRevision int `json:"recovery_revision"`
	LastSalonAt time.Time `json:"last_salon_at"`
	SilenceAge int `json:"silence_age_ms"`
	SilenceTimeout int `json:"silence_timeout_ms"`
	Rooms []recoveryRoomState `json:"rooms"`
	Timeline []recoveryEvent `json:"timeline"`
}

var controlledRecovery = struct {
	sync.RWMutex
	sourceEnabled bool
	recoveryMode string
	phase string
	freshEvidence int
	approved bool
	revision int
	lastSalonAt time.Time
	lastSalonValue float64
	rooms map[string]recoveryRoomState
	timeline []recoveryEvent
}{
	sourceEnabled: true,
	recoveryMode: "automatic",
	phase: "normal",
	lastSalonAt: time.Now().UTC(),
	rooms: make(map[string]recoveryRoomState),
}

func init() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil { log.Printf("[controlled-recovery] unavailable: %v", err); return }
	mux := http.NewServeMux()
	mux.HandleFunc("/", serveRecoveryPanel)
	mux.HandleFunc("/api/home", serveRecoveryState)
	mux.HandleFunc("/api/source/stop", stopRecoverySource)
	mux.HandleFunc("/api/source/resume", resumeRecoverySource)
	mux.HandleFunc("/api/recovery/automatic", useAutomaticRecovery)
	mux.HandleFunc("/api/recovery/manual", useManualRecovery)
	mux.HandleFunc("/api/recovery/approve", approveRecovery)
	mux.HandleFunc("/api/reset", resetRecovery)
	address := fmt.Sprintf("http://%s/", listener.Addr().String())
	log.Printf("PANEL %s", address)
	go func() {
		if err := http.Serve(listener, mux); err != nil && err != http.ErrServerClosed {
			log.Printf("[controlled-recovery] stopped: %v", err)
		}
	}()
}

func HomeRecovery_salon_available() bool {
	controlledRecovery.RLock()
	defer controlledRecovery.RUnlock()
	return controlledRecovery.sourceEnabled
}

func HomeRecovery_record_salon(observedAt any, airQuality float64) {
	observed := decodeRecoveryTime(observedAt)
	controlledRecovery.Lock()
	controlledRecovery.lastSalonAt = observed
	controlledRecovery.lastSalonValue = airQuality
	if controlledRecovery.phase == "validating" {
		controlledRecovery.freshEvidence++
		appendRecoveryEventLocked("Przyjęto świeży pomiar salonu", fmt.Sprintf("dowód %d/%d: %.0f ppm", controlledRecovery.freshEvidence, requiredFreshEvidence, airQuality), "sterowanie nadal wstrzymane")
		if controlledRecovery.freshEvidence >= requiredFreshEvidence {
			controlledRecovery.freshEvidence = requiredFreshEvidence
			if controlledRecovery.recoveryMode == "automatic" {
				controlledRecovery.phase = "normal"
				appendRecoveryEventLocked("Automatyczne recovery zakończone", "dwa kolejne świeże pomiary", "normalne sterowanie przywrócone")
			} else {
				controlledRecovery.phase = "awaiting-approval"
				appendRecoveryEventLocked("Warunki danych są spełnione", "dwa kolejne świeże pomiary", "oczekiwanie na zatwierdzenie człowieka")
			}
		}
	} else if controlledRecovery.phase == "approved" {
		controlledRecovery.phase = "normal"
		appendRecoveryEventLocked("Ręczne recovery zakończone", "zatwierdzenie i nowy świeży pomiar", "normalne sterowanie przywrócone")
	}
	controlledRecovery.Unlock()
}

func HomeRecovery_health() any {
	controlledRecovery.Lock()
	age := int(time.Since(controlledRecovery.lastSalonAt).Milliseconds())
	if age < 0 { age = 0 }
	status := "online"
	if !controlledRecovery.sourceEnabled && age <= int(recoverySilenceTimeout/time.Millisecond) {
		status = "waiting-for-timeout"
	}
	if !controlledRecovery.sourceEnabled && age > int(recoverySilenceTimeout/time.Millisecond) {
		status = "silent"
		if controlledRecovery.phase == "waiting" {
			controlledRecovery.phase = "limited"
			appendRecoveryEventLocked("Monitor potwierdził ciszę salonu", "brak pomiaru przez 4500 ms", "sterowanie salonem ograniczone")
		}
	}
	signal := recoverySignal{Kind: "source-health", Room: "salon", AirQuality: controlledRecovery.lastSalonValue, SourceStatus: status, SilenceAge: age, SilenceTimeout: int(recoverySilenceTimeout/time.Millisecond), ObservedAt: controlledRecovery.lastSalonAt}
	controlledRecovery.Unlock()
	return signal
}

func HomeRecovery_assess(value any) any {
	payload, _ := json.Marshal(value)
	var signal recoverySignal
	_ = json.Unmarshal(payload, &signal)
	controlledRecovery.RLock()
	allow := signal.Room != "salon" || (signal.Kind == "reading" && controlledRecovery.phase == "normal")
	result := recoveryAssessment{RecoveryMode: controlledRecovery.recoveryMode, RecoveryPhase: controlledRecovery.phase, FreshEvidence: controlledRecovery.freshEvidence, RequiredEvidence: requiredFreshEvidence, Approved: controlledRecovery.approved, AllowControl: allow, RecoveryRevision: controlledRecovery.revision}
	controlledRecovery.RUnlock()
	return result
}

func HomeRecovery_publish(value any) {
	payload, err := json.Marshal(value)
	if err != nil { return }
	var next recoveryRoomState
	if json.Unmarshal(payload, &next) != nil || next.Room == "" { return }
	controlledRecovery.Lock()
	controlledRecovery.rooms[next.Room] = next
	controlledRecovery.Unlock()
}

func stopRecoverySource(response http.ResponseWriter, request *http.Request) {
	if !requireRecoveryPost(response, request) { return }
	controlledRecovery.Lock()
	if controlledRecovery.sourceEnabled {
		controlledRecovery.sourceEnabled = false
		controlledRecovery.phase = "waiting"
		controlledRecovery.freshEvidence = 0
		controlledRecovery.approved = false
		controlledRecovery.revision++
		appendRecoveryEventLocked("Zatrzymano źródło salonu", "kontrolowane polecenie laboratorium", "rozpoczęto odmierzanie limitu ciszy")
	}
	controlledRecovery.Unlock()
	serveRecoveryState(response, request)
}

func resumeRecoverySource(response http.ResponseWriter, request *http.Request) {
	if !requireRecoveryPost(response, request) { return }
	controlledRecovery.Lock()
	if !controlledRecovery.sourceEnabled {
		controlledRecovery.sourceEnabled = true
		controlledRecovery.phase = "validating"
		controlledRecovery.freshEvidence = 0
		controlledRecovery.approved = false
		appendRecoveryEventLocked("Wznowiono źródło salonu", "źródło może ponownie publikować", "walidacja 0/2; sterowanie nadal wstrzymane")
	}
	controlledRecovery.Unlock()
	serveRecoveryState(response, request)
}

func useAutomaticRecovery(response http.ResponseWriter, request *http.Request) {
	setRecoveryMode(response, request, "automatic")
}

func useManualRecovery(response http.ResponseWriter, request *http.Request) {
	setRecoveryMode(response, request, "manual-approval")
}

func setRecoveryMode(response http.ResponseWriter, request *http.Request, mode string) {
	if !requireRecoveryPost(response, request) { return }
	controlledRecovery.Lock()
	if controlledRecovery.phase != "normal" {
		controlledRecovery.Unlock()
		http.Error(response, "tryb można zmienić tylko w stanie normalnym", http.StatusConflict)
		return
	}
	controlledRecovery.recoveryMode = mode
	controlledRecovery.approved = false
	appendRecoveryEventLocked("Wybrano tryb recovery", mode, "ustawienie obowiązuje w następnym cyklu")
	controlledRecovery.Unlock()
	serveRecoveryState(response, request)
}

func approveRecovery(response http.ResponseWriter, request *http.Request) {
	if !requireRecoveryPost(response, request) { return }
	controlledRecovery.Lock()
	if controlledRecovery.phase != "awaiting-approval" {
		controlledRecovery.Unlock()
		http.Error(response, "brak recovery oczekującego na zatwierdzenie", http.StatusConflict)
		return
	}
	controlledRecovery.approved = true
	controlledRecovery.phase = "approved"
	appendRecoveryEventLocked("Człowiek zatwierdził recovery", "warunki danych 2/2 zostały wcześniej spełnione", "oczekiwanie na kolejny świeży pomiar")
	controlledRecovery.Unlock()
	serveRecoveryState(response, request)
}

func resetRecovery(response http.ResponseWriter, request *http.Request) {
	if !requireRecoveryPost(response, request) { return }
	controlledRecovery.Lock()
	controlledRecovery.sourceEnabled = true
	controlledRecovery.phase = "normal"
	controlledRecovery.freshEvidence = 0
	controlledRecovery.approved = false
	controlledRecovery.lastSalonAt = time.Now().UTC()
	controlledRecovery.timeline = nil
	appendRecoveryEventLocked("Przywrócono punkt odniesienia", "reset laboratorium", "źródło działa; sterowanie jest normalne")
	controlledRecovery.Unlock()
	serveRecoveryState(response, request)
}

func requireRecoveryPost(response http.ResponseWriter, request *http.Request) bool {
	if request.Method == http.MethodPost { return true }
	response.Header().Set("Allow", http.MethodPost)
	http.Error(response, "method not allowed", http.StatusMethodNotAllowed)
	return false
}

func appendRecoveryEventLocked(title, evidence, effect string) {
	controlledRecovery.timeline = append(controlledRecovery.timeline, recoveryEvent{Step: len(controlledRecovery.timeline) + 1, Title: title, Evidence: evidence, Effect: effect, OccurredAt: time.Now().UTC()})
	if len(controlledRecovery.timeline) > 14 {
		controlledRecovery.timeline = append([]recoveryEvent{}, controlledRecovery.timeline[len(controlledRecovery.timeline)-14:]...)
		for index := range controlledRecovery.timeline { controlledRecovery.timeline[index].Step = index + 1 }
	}
}

func decodeRecoveryTime(value any) time.Time {
	if observed, ok := value.(time.Time); ok { return observed }
	payload, err := json.Marshal(value)
	if err == nil {
		var observed time.Time
		if json.Unmarshal(payload, &observed) == nil && !observed.IsZero() { return observed }
	}
	return time.Now().UTC()
}

func serveRecoveryState(response http.ResponseWriter, _ *http.Request) {
	controlledRecovery.RLock()
	rooms := make([]recoveryRoomState, 0, len(controlledRecovery.rooms))
	for _, room := range controlledRecovery.rooms { rooms = append(rooms, room) }
	sort.Slice(rooms, func(left, right int) bool { return recoveryRoomRank(rooms[left].Room) < recoveryRoomRank(rooms[right].Room) })
	age := int(time.Since(controlledRecovery.lastSalonAt).Milliseconds())
	if age < 0 { age = 0 }
	result := recoveryResponse{SourceEnabled: controlledRecovery.sourceEnabled, RecoveryMode: controlledRecovery.recoveryMode, RecoveryPhase: controlledRecovery.phase, FreshEvidence: controlledRecovery.freshEvidence, RequiredEvidence: requiredFreshEvidence, Approved: controlledRecovery.approved, RecoveryRevision: controlledRecovery.revision, LastSalonAt: controlledRecovery.lastSalonAt, SilenceAge: age, SilenceTimeout: int(recoverySilenceTimeout/time.Millisecond), Rooms: rooms, Timeline: append([]recoveryEvent{}, controlledRecovery.timeline...)}
	controlledRecovery.RUnlock()
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_ = json.NewEncoder(response).Encode(result)
}

func recoveryRoomRank(room string) int {
	switch room { case "salon": return 0; case "kuchnia": return 1; case "sypialnia": return 2; default: return 10 }
}

func serveRecoveryPanel(response http.ResponseWriter, request *http.Request) {
	if request.URL.Path != "/" { http.NotFound(response, request); return }
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_, _ = response.Write([]byte(controlledRecoveryHTML))
}

const controlledRecoveryHTML = `<!doctype html><html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Dom 8 · Kontrolowany powrót</title>
<style>:root{color-scheme:dark;font-family:Inter,ui-sans-serif,system-ui,sans-serif;background:#07111c;color:#edf8ff;--cyan:#6bdcf8;--mint:#5be3aa;--amber:#ffc86e;--red:#ff8a82;--panel:#0d2232;--line:#285069}*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at 15% 0,#174668 0,#07111c 42%);min-height:100vh}main{width:min(1160px,calc(100% - 30px));margin:auto;padding:42px 0 70px}.eyebrow,.label{color:#8bd3ec;text-transform:uppercase;letter-spacing:.13em;font-size:.72rem;font-weight:850}h1{font-size:clamp(2.25rem,5vw,4rem);line-height:1;margin:8px 0}.subtitle{color:#a8bfd0;max-width:760px;line-height:1.55}.top{display:flex;justify-content:space-between;align-items:end;gap:20px;margin-bottom:24px}.badge{border:1px solid #39708e;border-radius:99px;padding:10px 15px;background:#102a3d;font-weight:800}.badge.limited,.badge.waiting{border-color:#a86b3d;color:#ffd49e;background:#352416}.badge.validating,.badge.awaiting-approval,.badge.approved{border-color:#357fc0;color:#bfe6ff;background:#102b45}.grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}.panel{background:rgba(11,30,45,.95);border:1px solid var(--line);border-radius:20px;padding:22px;box-shadow:0 20px 45px #0003}.panel h2{margin:6px 0 10px}.wide{grid-column:1/-1}.modes,.actions{display:flex;gap:9px;flex-wrap:wrap;margin-top:16px}button{border:1px solid #3b6680;border-radius:11px;padding:11px 14px;background:#123047;color:#eefaff;font:inherit;font-weight:780;cursor:pointer}button.primary{background:#087d61;border-color:#46d4a9}button.danger{background:#893c34;border-color:#dd7166}button:disabled{opacity:.4;cursor:not-allowed}.meter{display:grid;grid-template-columns:repeat(2,1fr);gap:10px;margin:17px 0}.proof{border:1px solid #31546a;border-radius:14px;padding:15px;background:#0a1b29}.proof strong{font-size:1.8rem;display:block;margin-top:4px}.bar{height:12px;border-radius:99px;background:#07131d;overflow:hidden;border:1px solid #2b4b60}.bar span{display:block;height:100%;background:linear-gradient(90deg,var(--cyan),var(--mint));transition:width .35s}.rooms{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}.room{border:1px solid #284d63;border-radius:15px;padding:16px;background:#0a1b29}.room h3{margin:0 0 12px;text-transform:capitalize}.room .value{font-size:1.75rem;font-weight:850}.on{color:var(--mint)}.off{color:var(--amber)}.timeline{display:grid;gap:10px;margin-top:15px}.event{display:grid;grid-template-columns:35px 1fr;gap:12px}.step{display:grid;place-items:center;width:30px;height:30px;border-radius:50%;background:#113c55;border:1px solid #3d7898;font-weight:850}.event strong{display:block}.event small{color:#9fb8ca;line-height:1.45}.empty{color:#92aabd}.contract{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:14px}.contract div{border-top:3px solid #35718e;background:#0a1a27;padding:12px;border-radius:8px}.contract b{display:block;margin-bottom:5px}.contract span{color:#9fb8ca;font-size:.86rem}@media(max-width:800px){.grid,.rooms,.contract{grid-template-columns:1fr}.wide{grid-column:auto}.top{display:block}.badge{display:inline-block;margin-top:15px}}</style></head>
<body><main><header class="top"><div><div class="eyebrow">Laboratorium Junexis · Dom 8</div><h1>Kontrolowany powrót</h1><p class="subtitle">Źródło może znowu mówić, zanim system pozwoli mu sterować. Zbieramy dowód, sprawdzamy politykę i dopiero wtedy oddajemy kontrolę.</p></div><div id="phase" class="badge">Ładowanie…</div></header>
<section class="grid"><article class="panel"><div class="label">1 · Polityka</div><h2>Kto kończy recovery?</h2><p class="subtitle">Tryb można wybrać w normalnym stanie, przed rozpoczęciem scenariusza.</p><div class="modes"><button id="automatic">Automatycznie po 2/2</button><button id="manual">Po zatwierdzeniu człowieka</button></div><p id="modeText"></p></article>
<article class="panel"><div class="label">2 · Dowód powrotu</div><h2 id="evidenceTitle">Świeże pomiary 0/2</h2><div class="bar"><span id="evidenceBar" style="width:0"></span></div><div class="meter"><div class="proof"><span class="label">Źródło</span><strong id="source">—</strong></div><div class="proof"><span class="label">Zatwierdzenie</span><strong id="approval">—</strong></div></div></article>
<article class="panel wide"><div class="label">Rytuał</div><h2>Przejdź przez granice świadomie</h2><div class="contract"><div><b>1. Utrata</b><span>monitor potwierdza ciszę</span></div><div><b>2. Powrót</b><span>źródło znów publikuje</span></div><div><b>3. Walidacja</b><span>dwa świeże pomiary</span></div><div><b>4. Sterowanie</b><span>polityka dopuszcza automatykę</span></div></div><div class="actions"><button class="danger" id="stop">Zatrzymaj źródło salonu</button><button class="primary" id="resume">Wznów źródło</button><button id="approve">Zatwierdź recovery</button><button id="reset">Reset</button></div></article>
<article class="panel wide"><div class="label">Rzeczywisty skutek</div><h2>Stan pomieszczeń</h2><div id="rooms" class="rooms"></div></article>
<article class="panel wide"><div class="label">Dowód przyczynowy</div><h2>Oś recovery</h2><div id="timeline" class="timeline"></div></article></section></main>
<script>const $=id=>document.getElementById(id);let current=null;const labels={normal:'normalne sterowanie',waiting:'odmierzanie ciszy',limited:'stan ograniczony',validating:'walidacja świeżych danych','awaiting-approval':'warunki spełnione · czeka na człowieka',approved:'zatwierdzone · czeka na nowy pomiar'};async function post(path){const response=await fetch(path,{method:'POST'});if(!response.ok)throw new Error(await response.text());render(await response.json())}function roomCard(room){return '<div class="room"><h3>'+room.room+'</h3><div class="value">'+room.air_quality+' ppm</div><p>decyzja: <b>'+room.decision+'</b></p><p>wykonanie: '+room.status+'</p><p class="'+(room.actual_enabled?'on':'off')+'">wentylacja: <b>'+(room.actual_enabled?'WŁĄCZONA':'WYŁĄCZONA')+'</b></p></div>'}function render(state){current=state;$('phase').textContent=labels[state.recovery_phase]||state.recovery_phase;$('phase').className='badge '+state.recovery_phase;$('modeText').textContent=state.recovery_mode==='automatic'?'System sam dopuści sterowanie po dwóch świeżych pomiarach.':'Po dwóch pomiarach system zaczeka na świadomą decyzję człowieka.';$('evidenceTitle').textContent='Świeże pomiary '+state.fresh_evidence+'/'+state.required_evidence;$('evidenceBar').style.width=(state.fresh_evidence/state.required_evidence*100)+'%';$('source').textContent=state.source_enabled?'aktywne':'milczy';$('approval').textContent=state.approved?'udzielone':(state.recovery_mode==='automatic'?'niepotrzebne':'brak');$('rooms').innerHTML=state.rooms.length?state.rooms.map(roomCard).join(''):'<p class="empty">Czekam na pierwsze potwierdzenia przepływu…</p>';$('timeline').innerHTML=state.timeline.length?state.timeline.map(event=>'<div class="event"><span class="step">'+event.step+'</span><div><strong>'+event.title+'</strong><small>'+event.evidence+' → '+event.effect+'</small></div></div>').join(''):'<p class="empty">Oś wypełni się podczas scenariusza.</p>';$('stop').disabled=!state.source_enabled||state.recovery_phase!=='normal';$('resume').disabled=state.source_enabled||!['waiting','limited'].includes(state.recovery_phase);$('approve').disabled=state.recovery_phase!=='awaiting-approval';$('automatic').disabled=state.recovery_phase!=='normal'||state.recovery_mode==='automatic';$('manual').disabled=state.recovery_phase!=='normal'||state.recovery_mode==='manual-approval'}async function refresh(){try{render(await(await fetch('/api/home',{cache:'no-store'})).json())}catch(error){console.error(error)}}$('stop').onclick=()=>post('/api/source/stop');$('resume').onclick=()=>post('/api/source/resume');$('approve').onclick=()=>post('/api/recovery/approve');$('automatic').onclick=()=>post('/api/recovery/automatic');$('manual').onclick=()=>post('/api/recovery/manual');$('reset').onclick=()=>post('/api/reset');refresh();setInterval(refresh,500);</script></body></html>`

