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

const policySilenceTimeout = 4500 * time.Millisecond

type policySignal struct {
	Kind string `json:"kind"`
	Room string `json:"room"`
	AirQuality float64 `json:"air_quality"`
	SourceStatus string `json:"source_status"`
	ObservedAt time.Time `json:"observed_at"`
}

type policyDecision struct {
	Room string `json:"room"`
	AirQuality float64 `json:"air_quality"`
	Outcome string `json:"outcome"`
	ActualEnabled bool `json:"actual_enabled"`
	ExecutionStatus string `json:"execution_status"`
	RecoveryMode string `json:"recovery_mode"`
	RecoveryPhase string `json:"recovery_phase"`
	FreshEvidence int `json:"fresh_evidence"`
	RequiredEvidence int `json:"required_evidence"`
	StabilizationDelay int `json:"stabilization_delay_ms"`
	EvidenceLimit int `json:"evidence_limit"`
	PolicyRevision int `json:"policy_revision"`
	Reason string `json:"reason"`
	DecidedAt time.Time `json:"decided_at"`
}

type policyEvent struct {
	Number int `json:"number"`
	Title string `json:"title"`
	Cause string `json:"cause"`
	Effect string `json:"effect"`
	OccurredAt time.Time `json:"occurred_at"`
}

type policyConfig struct {
	RecoveryMode string `json:"recovery_mode"`
	RequiredEvidence int `json:"required_evidence"`
	StabilizationDelay int `json:"stabilization_delay_ms"`
	EvidenceLimit int `json:"evidence_limit"`
}

type policyResponse struct {
	SourceEnabled bool `json:"source_enabled"`
	RecoveryPhase string `json:"recovery_phase"`
	FreshEvidence int `json:"fresh_evidence"`
	Approved bool `json:"approved"`
	PolicyRevision int `json:"policy_revision"`
	RecoveryRevision int `json:"recovery_revision"`
	SilenceAge int `json:"silence_age_ms"`
	SilenceTimeout int `json:"silence_timeout_ms"`
	Config policyConfig `json:"effective_config"`
	Rooms []policyDecision `json:"rooms"`
	Timeline []policyEvent `json:"timeline"`
}

var recoveryPolicy = struct {
	sync.RWMutex
	sourceEnabled bool
	phase string
	freshEvidence int
	approved bool
	policyRevision int
	recoveryRevision int
	timerGeneration int
	lastSalonAt time.Time
	lastSalonValue float64
	config policyConfig
	rooms map[string]policyDecision
	timeline []policyEvent
	eventNumber int
}{
	sourceEnabled: true,
	phase: "normal",
	lastSalonAt: time.Now().UTC(),
	config: policyConfig{RecoveryMode: "manual-approval", RequiredEvidence: 2, StabilizationDelay: 2000, EvidenceLimit: 18},
	rooms: make(map[string]policyDecision),
}

func init() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil { log.Printf("[recovery-policy] unavailable: %v", err); return }
	mux := http.NewServeMux()
	mux.HandleFunc("/", servePolicyPanel)
	mux.HandleFunc("/api/home", servePolicyState)
	mux.HandleFunc("/api/policy", applyPolicy)
	mux.HandleFunc("/api/source/stop", stopPolicySource)
	mux.HandleFunc("/api/source/resume", resumePolicySource)
	mux.HandleFunc("/api/recovery/approve", approvePolicyRecovery)
	mux.HandleFunc("/api/recovery/reject", rejectPolicyRecovery)
	mux.HandleFunc("/api/recovery/retry", retryPolicyRecovery)
	mux.HandleFunc("/api/rollback", rollbackPolicy)
	address := fmt.Sprintf("http://%s/", listener.Addr().String())
	log.Printf("PANEL %s", address)
	go func() {
		if err := http.Serve(listener, mux); err != nil && err != http.ErrServerClosed {
			log.Printf("[recovery-policy] stopped: %v", err)
		}
	}()
}

func HomePolicy_salon_available() bool {
	recoveryPolicy.RLock()
	defer recoveryPolicy.RUnlock()
	return recoveryPolicy.sourceEnabled
}

func HomePolicy_record_salon(observedAt any, airQuality float64) {
	observed := decodePolicyTime(observedAt)
	recoveryPolicy.Lock()
	recoveryPolicy.lastSalonAt = observed
	recoveryPolicy.lastSalonValue = airQuality
	switch recoveryPolicy.phase {
	case "validating":
		recoveryPolicy.freshEvidence++
		appendPolicyEventLocked("Przyjęto świeży dowód", fmt.Sprintf("pomiar %d/%d", recoveryPolicy.freshEvidence, recoveryPolicy.config.RequiredEvidence), "sterowanie salonem pozostaje wstrzymane")
		if recoveryPolicy.freshEvidence >= recoveryPolicy.config.RequiredEvidence {
			recoveryPolicy.freshEvidence = recoveryPolicy.config.RequiredEvidence
			if recoveryPolicy.config.RecoveryMode == "automatic" {
				beginStabilizationLocked()
			} else {
				recoveryPolicy.phase = "awaiting-approval"
				appendPolicyEventLocked("Warunek danych jest spełniony", "komplet świeżych pomiarów", "oczekiwanie na decyzję człowieka")
			}
		}
	case "ready-automatic":
		recoveryPolicy.phase = "normal"
		appendPolicyEventLocked("Automatyczne recovery zakończone", "czas stabilizacji upłynął i nadszedł nowy pomiar", "normalne sterowanie przywrócone")
	case "approved":
		recoveryPolicy.phase = "normal"
		appendPolicyEventLocked("Ręczne recovery zakończone", "zatwierdzenie i nowy pomiar", "normalne sterowanie przywrócone")
	}
	recoveryPolicy.Unlock()
}

func beginStabilizationLocked() {
	recoveryPolicy.phase = "stabilizing"
	recoveryPolicy.timerGeneration++
	generation := recoveryPolicy.timerGeneration
	recoveryRevision := recoveryPolicy.recoveryRevision
	delay := recoveryPolicy.config.StabilizationDelay
	appendPolicyEventLocked("Rozpoczęto czas stabilizacji", fmt.Sprintf("%d ms po komplecie dowodów", delay), "sterowanie nadal wstrzymane")
	go func() {
		time.Sleep(time.Duration(delay) * time.Millisecond)
		recoveryPolicy.Lock()
		defer recoveryPolicy.Unlock()
		if recoveryPolicy.timerGeneration != generation || recoveryPolicy.recoveryRevision != recoveryRevision || recoveryPolicy.phase != "stabilizing" {
			return
		}
		recoveryPolicy.phase = "ready-automatic"
		appendPolicyEventLocked("Czas stabilizacji upłynął", fmt.Sprintf("%d ms bez zmiany polityki", delay), "oczekiwanie na następny świeży pomiar")
	}()
}

func HomePolicy_health() any {
	recoveryPolicy.Lock()
	age := int(time.Since(recoveryPolicy.lastSalonAt).Milliseconds())
	if age < 0 { age = 0 }
	status := "online"
	if !recoveryPolicy.sourceEnabled && age <= int(policySilenceTimeout/time.Millisecond) { status = "waiting-for-timeout" }
	if !recoveryPolicy.sourceEnabled && age > int(policySilenceTimeout/time.Millisecond) {
		status = "silent"
		if recoveryPolicy.phase == "waiting" {
			recoveryPolicy.phase = "limited"
			appendPolicyEventLocked("Monitor potwierdził ciszę", "brak pomiaru przez 4500 ms", "salon przeszedł do stanu ograniczonego")
		}
	}
	signal := policySignal{Kind: "source-health", Room: "salon", AirQuality: recoveryPolicy.lastSalonValue, SourceStatus: status, ObservedAt: recoveryPolicy.lastSalonAt}
	recoveryPolicy.Unlock()
	return signal
}

func HomePolicy_evaluate(value any) any {
	payload, _ := json.Marshal(value)
	var signal policySignal
	_ = json.Unmarshal(payload, &signal)
	recoveryPolicy.RLock()
	defer recoveryPolicy.RUnlock()
	decision := policyDecision{Room: signal.Room, AirQuality: signal.AirQuality, RecoveryMode: recoveryPolicy.config.RecoveryMode, RecoveryPhase: recoveryPolicy.phase, FreshEvidence: recoveryPolicy.freshEvidence, RequiredEvidence: recoveryPolicy.config.RequiredEvidence, StabilizationDelay: recoveryPolicy.config.StabilizationDelay, EvidenceLimit: recoveryPolicy.config.EvidenceLimit, PolicyRevision: recoveryPolicy.policyRevision, DecidedAt: time.Now().UTC()}
	previous := recoveryPolicy.rooms[signal.Room]
	decision.ActualEnabled = previous.ActualEnabled
	if signal.Kind == "source-health" && signal.SourceStatus != "silent" {
		decision.Room = ""
		decision.Outcome = "ignored-health"
		decision.ExecutionStatus = "ignored"
		decision.Reason = "monitor nie potwierdził ciszy"
		return decision
	}
	if signal.Kind == "source-health" && signal.SourceStatus == "silent" {
		decision.Outcome = "hold-source-silent"
		decision.ExecutionStatus = "limited-source-silent"
		decision.Reason = "jawna polityka zachowuje ostatni potwierdzony stan"
		return decision
	}
	if signal.Room == "salon" && recoveryPolicy.phase != "normal" {
		decision.Outcome = "hold-policy-gate"
		decision.ExecutionStatus = "held-by-recovery-policy"
		decision.Reason = "efektywna polityka nie oddała jeszcze sterowania"
		return decision
	}
	if signal.AirQuality > 1000 {
		decision.Outcome = "enable-ventilation"
		decision.ActualEnabled = true
	} else {
		decision.Outcome = "observe"
		decision.ActualEnabled = false
	}
	decision.ExecutionStatus = "applied"
	decision.Reason = "normalna reguła jakości powietrza"
	return decision
}

func HomePolicy_publish(value any) {
	payload, err := json.Marshal(value)
	if err != nil { return }
	var decision policyDecision
	if json.Unmarshal(payload, &decision) != nil || decision.Room == "" { return }
	recoveryPolicy.Lock()
	recoveryPolicy.rooms[decision.Room] = decision
	recoveryPolicy.Unlock()
}

func applyPolicy(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	var next policyConfig
	if json.NewDecoder(request.Body).Decode(&next) != nil {
		http.Error(response, "niepoprawna konfiguracja", http.StatusBadRequest); return
	}
	if (next.RecoveryMode != "automatic" && next.RecoveryMode != "manual-approval") || next.RequiredEvidence < 2 || next.RequiredEvidence > 5 || next.StabilizationDelay < 250 || next.StabilizationDelay > 10000 || next.EvidenceLimit < 6 || next.EvidenceLimit > 60 {
		http.Error(response, "konfiguracja poza dozwolonym zakresem", http.StatusBadRequest); return
	}
	recoveryPolicy.Lock()
	if recoveryPolicy.phase != "normal" {
		recoveryPolicy.Unlock(); http.Error(response, "politykę można zmienić tylko w stanie normalnym", http.StatusConflict); return
	}
	recoveryPolicy.config = next
	recoveryPolicy.policyRevision++
	appendPolicyEventLocked("Zastosowano pełną politykę", fmt.Sprintf("tryb=%s, dowody=%d, stabilizacja=%d ms, retencja=%d", next.RecoveryMode, next.RequiredEvidence, next.StabilizationDelay, next.EvidenceLimit), "następny cykl recovery użyje tej rewizji")
	trimPolicyEventsLocked()
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func stopPolicySource(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	recoveryPolicy.Lock()
	if recoveryPolicy.phase != "normal" || !recoveryPolicy.sourceEnabled {
		recoveryPolicy.Unlock(); http.Error(response, "źródło można zatrzymać tylko w stanie normalnym", http.StatusConflict); return
	}
	recoveryPolicy.sourceEnabled = false
	recoveryPolicy.phase = "waiting"
	recoveryPolicy.freshEvidence = 0
	recoveryPolicy.approved = false
	recoveryPolicy.recoveryRevision++
	recoveryPolicy.timerGeneration++
	appendPolicyEventLocked("Zatrzymano źródło salonu", fmt.Sprintf("cykl recovery #%d", recoveryPolicy.recoveryRevision), "odmierzanie limitu ciszy")
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func resumePolicySource(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	recoveryPolicy.Lock()
	if recoveryPolicy.sourceEnabled || (recoveryPolicy.phase != "waiting" && recoveryPolicy.phase != "limited") {
		recoveryPolicy.Unlock(); http.Error(response, "źródło nie oczekuje na wznowienie", http.StatusConflict); return
	}
	recoveryPolicy.sourceEnabled = true
	recoveryPolicy.phase = "validating"
	recoveryPolicy.freshEvidence = 0
	appendPolicyEventLocked("Wznowiono źródło salonu", "źródło ponownie publikuje", fmt.Sprintf("walidacja 0/%d", recoveryPolicy.config.RequiredEvidence))
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func approvePolicyRecovery(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	recoveryPolicy.Lock()
	if recoveryPolicy.phase != "awaiting-approval" {
		recoveryPolicy.Unlock(); http.Error(response, "brak recovery oczekującego na zatwierdzenie", http.StatusConflict); return
	}
	recoveryPolicy.approved = true
	recoveryPolicy.phase = "approved"
	appendPolicyEventLocked("Człowiek zatwierdził recovery", "pełny dowód był dostępny przed decyzją", "oczekiwanie na nowy pomiar")
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func rejectPolicyRecovery(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	recoveryPolicy.Lock()
	if recoveryPolicy.phase != "awaiting-approval" {
		recoveryPolicy.Unlock(); http.Error(response, "brak recovery oczekującego na odrzucenie", http.StatusConflict); return
	}
	recoveryPolicy.approved = false
	recoveryPolicy.phase = "rejected"
	appendPolicyEventLocked("Człowiek odrzucił recovery", "dowód danych nie wystarczył do przejęcia odpowiedzialności", "sterowanie pozostaje wstrzymane")
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func retryPolicyRecovery(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	recoveryPolicy.Lock()
	if recoveryPolicy.phase != "rejected" || !recoveryPolicy.sourceEnabled {
		recoveryPolicy.Unlock(); http.Error(response, "ponowienie wymaga odrzuconego recovery i aktywnego źródła", http.StatusConflict); return
	}
	recoveryPolicy.phase = "validating"
	recoveryPolicy.freshEvidence = 0
	appendPolicyEventLocked("Rozpoczęto ponowną walidację", "poprzedni dowód został świadomie odrzucony", fmt.Sprintf("nowy licznik 0/%d", recoveryPolicy.config.RequiredEvidence))
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func rollbackPolicy(response http.ResponseWriter, request *http.Request) {
	if !requirePolicyPost(response, request) { return }
	recoveryPolicy.Lock()
	recoveryPolicy.timerGeneration++
	recoveryPolicy.sourceEnabled = true
	recoveryPolicy.phase = "normal"
	recoveryPolicy.freshEvidence = 0
	recoveryPolicy.approved = false
	recoveryPolicy.lastSalonAt = time.Now().UTC()
	recoveryPolicy.config = policyConfig{RecoveryMode: "manual-approval", RequiredEvidence: 2, StabilizationDelay: 2000, EvidenceLimit: 18}
	recoveryPolicy.policyRevision++
	recoveryPolicy.timeline = nil
	recoveryPolicy.eventNumber = 0
	appendPolicyEventLocked("Wykonano rollback polityki", "unieważniono trwające recovery i ustawienia eksperckie", "przywrócono bezpieczny profil bazowy")
	recoveryPolicy.Unlock()
	servePolicyState(response, request)
}

func appendPolicyEventLocked(title, cause, effect string) {
	recoveryPolicy.eventNumber++
	recoveryPolicy.timeline = append(recoveryPolicy.timeline, policyEvent{Number: recoveryPolicy.eventNumber, Title: title, Cause: cause, Effect: effect, OccurredAt: time.Now().UTC()})
	trimPolicyEventsLocked()
}

func trimPolicyEventsLocked() {
	limit := recoveryPolicy.config.EvidenceLimit
	if len(recoveryPolicy.timeline) > limit {
		recoveryPolicy.timeline = append([]policyEvent{}, recoveryPolicy.timeline[len(recoveryPolicy.timeline)-limit:]...)
	}
}

func requirePolicyPost(response http.ResponseWriter, request *http.Request) bool {
	if request.Method == http.MethodPost { return true }
	response.Header().Set("Allow", http.MethodPost)
	http.Error(response, "method not allowed", http.StatusMethodNotAllowed)
	return false
}

func decodePolicyTime(value any) time.Time {
	if observed, ok := value.(time.Time); ok { return observed }
	payload, err := json.Marshal(value)
	if err == nil {
		var observed time.Time
		if json.Unmarshal(payload, &observed) == nil && !observed.IsZero() { return observed }
	}
	return time.Now().UTC()
}

func servePolicyState(response http.ResponseWriter, _ *http.Request) {
	recoveryPolicy.RLock()
	rooms := make([]policyDecision, 0, len(recoveryPolicy.rooms))
	for _, room := range recoveryPolicy.rooms { rooms = append(rooms, room) }
	sort.Slice(rooms, func(left, right int) bool { return policyRoomRank(rooms[left].Room) < policyRoomRank(rooms[right].Room) })
	age := int(time.Since(recoveryPolicy.lastSalonAt).Milliseconds())
	if age < 0 { age = 0 }
	result := policyResponse{SourceEnabled: recoveryPolicy.sourceEnabled, RecoveryPhase: recoveryPolicy.phase, FreshEvidence: recoveryPolicy.freshEvidence, Approved: recoveryPolicy.approved, PolicyRevision: recoveryPolicy.policyRevision, RecoveryRevision: recoveryPolicy.recoveryRevision, SilenceAge: age, SilenceTimeout: int(policySilenceTimeout/time.Millisecond), Config: recoveryPolicy.config, Rooms: rooms, Timeline: append([]policyEvent{}, recoveryPolicy.timeline...)}
	recoveryPolicy.RUnlock()
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_ = json.NewEncoder(response).Encode(result)
}

func policyRoomRank(room string) int {
	switch room { case "salon": return 0; case "kuchnia": return 1; case "sypialnia": return 2; default: return 10 }
}

func servePolicyPanel(response http.ResponseWriter, request *http.Request) {
	if request.URL.Path != "/" { http.NotFound(response, request); return }
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_, _ = response.Write([]byte(recoveryPolicyHTML))
}

const recoveryPolicyHTML = `<!doctype html><html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Dom 9 · Polityka recovery</title>
<style>:root{color-scheme:dark;font-family:Inter,ui-sans-serif,system-ui,sans-serif;background:#081018;color:#eef8ff;--cyan:#76ddfa;--mint:#5fe0a9;--amber:#ffc86d;--red:#ff8b82;--panel:#0d202e;--line:#2a5065}*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(circle at 15% 0,#173f5e 0,#081018 42%)}main{width:min(1180px,calc(100% - 30px));margin:auto;padding:42px 0 70px}.eyebrow,.label{color:#8cd5ed;text-transform:uppercase;letter-spacing:.13em;font-size:.72rem;font-weight:850}h1{font-size:clamp(2.3rem,5vw,4rem);line-height:1.02;margin:8px 0;max-width:850px}.subtitle,.muted{color:#a4bdce;line-height:1.55}.top{display:flex;justify-content:space-between;align-items:end;gap:20px;margin-bottom:24px}.phase{border:1px solid #40738e;border-radius:99px;padding:10px 15px;background:#102a3c;font-weight:800}.grid{display:grid;grid-template-columns:1.05fr .95fr;gap:16px}.card{background:rgba(12,29,42,.96);border:1px solid var(--line);border-radius:20px;padding:22px;box-shadow:0 20px 45px #0003}.wide{grid-column:1/-1}.fields{display:grid;grid-template-columns:repeat(2,1fr);gap:12px;margin:16px 0}.field{display:grid;gap:7px;color:#b8cedc;font-weight:700}.field input,.field select{width:100%;border:1px solid #376078;border-radius:10px;background:#091b29;color:#eef8ff;padding:11px;font:inherit}.actions{display:flex;gap:9px;flex-wrap:wrap;margin-top:15px}button{border:1px solid #3b6881;border-radius:11px;padding:11px 14px;background:#123148;color:#effaff;font:inherit;font-weight:790;cursor:pointer}button.primary{background:#087b60;border-color:#42d1a4}button.danger{background:#893b34;border-color:#de7167}button:disabled{opacity:.38;cursor:not-allowed}.evidence{font-size:2.6rem;font-weight:880;margin:7px 0}.bar{height:12px;border:1px solid #315469;border-radius:99px;background:#07151f;overflow:hidden}.bar span{display:block;height:100%;background:linear-gradient(90deg,var(--cyan),var(--mint));transition:width .3s}.effective{background:#071620;border:1px solid #27485b;border-radius:13px;padding:16px;white-space:pre-wrap;color:#cbe9f7;line-height:1.5}.rooms{display:grid;grid-template-columns:repeat(3,1fr);gap:11px}.room{border:1px solid #294c60;border-radius:14px;background:#091a26;padding:15px}.room h3{margin:0 0 9px;text-transform:capitalize}.room strong{font-size:1.55rem}.on{color:var(--mint)}.off{color:var(--amber)}.timeline{display:grid;gap:10px;margin-top:15px}.event{display:grid;grid-template-columns:44px 1fr;gap:11px;padding:11px;border-bottom:1px solid #1d3a4b}.number{color:var(--cyan);font-weight:850}.event strong{display:block}.event small{color:#9db5c6}.empty{color:#8fa8ba}@media(max-width:800px){.grid,.fields,.rooms{grid-template-columns:1fr}.wide{grid-column:auto}.top{display:block}.phase{display:inline-block;margin-top:14px}}</style></head>
<body><main><header class="top"><div><div class="eyebrow">Laboratorium Junexis · Dom 9</div><h1>Ty definiujesz warunki powrotu</h1><p class="subtitle">Pełna polityka jest jawna, wersjonowana i sprawdzana przez działający system. Odrzucenie nie znika z historii, a rollback unieważnia trwające recovery.</p></div><div id="phase" class="phase">Ładowanie…</div></header>
<section class="grid"><article class="card"><div class="label">Polityka eksperta</div><h2>Zapisz kontrakt przed awarią</h2><div class="fields"><label class="field">Właściciel decyzji<select id="mode"><option value="manual-approval">człowiek zatwierdza</option><option value="automatic">system automatyczny</option></select></label><label class="field">Świeże dowody<input id="required" type="number" min="2" max="5" value="2"></label><label class="field">Czas stabilizacji (ms)<input id="delay" type="number" min="250" max="10000" step="250" value="2000"></label><label class="field">Limit osi dowodowej<input id="limit" type="number" min="6" max="60" value="18"></label></div><div class="actions"><button class="primary" id="apply">Zastosuj pełną politykę</button><button id="rollback">Rollback do profilu bazowego</button></div><p id="policyResult" class="muted"></p></article>
<article class="card"><div class="label">Efektywna konfiguracja</div><h2>To naprawdę wykonuje system</h2><pre id="effective" class="effective">Oczekiwanie…</pre><div class="label">Postęp bieżącego recovery</div><div id="evidence" class="evidence">0/2</div><div class="bar"><span id="bar" style="width:0"></span></div></article>
<article class="card wide"><div class="label">Rytuał operacyjny</div><h2>Utrata → dowód → decyzja → powrót</h2><div class="actions"><button class="danger" id="stop">Zatrzymaj źródło</button><button class="primary" id="resume">Wznów źródło</button><button id="approve">Zatwierdź recovery</button><button class="danger" id="reject">Odrzuć recovery</button><button id="retry">Zbierz dowód ponownie</button></div><p id="ritual" class="muted"></p></article>
<article class="card wide"><div class="label">Rzeczywisty skutek</div><h2>Stan domu</h2><div id="rooms" class="rooms"></div></article>
<article class="card wide"><div class="label">Retencja</div><h2>Oś decyzji i dowodów</h2><div id="timeline" class="timeline"></div></article></section></main>
<script>const $=id=>document.getElementById(id);let state=null;const phaseNames={normal:'normalne sterowanie',waiting:'odmierzanie ciszy',limited:'stan ograniczony',validating:'zbieranie świeżych dowodów',stabilizing:'czas stabilizacji','ready-automatic':'warunki automatyczne spełnione','awaiting-approval':'czeka na decyzję człowieka',approved:'zatwierdzone · czeka na pomiar',rejected:'recovery odrzucone'};async function post(path,body={}){const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});if(!r.ok)throw Error(await r.text());render(await r.json())}function room(x){return '<div class="room"><h3>'+x.room+'</h3><strong>'+x.air_quality+' ppm</strong><p>'+x.outcome+'</p><p>'+x.execution_status+'</p><p class="'+(x.actual_enabled?'on':'off')+'">wentylacja: '+(x.actual_enabled?'WŁĄCZONA':'WYŁĄCZONA')+'</p></div>'}function render(s){state=s;const c=s.effective_config;$('phase').textContent=phaseNames[s.recovery_phase]||s.recovery_phase;$('evidence').textContent=s.fresh_evidence+'/'+c.required_evidence;$('bar').style.width=(s.fresh_evidence/c.required_evidence*100)+'%';$('effective').textContent=JSON.stringify({policy_revision:s.policy_revision,recovery_mode:c.recovery_mode,required_evidence:c.required_evidence,stabilization_delay_ms:c.stabilization_delay_ms,evidence_limit:c.evidence_limit},null,2);if(document.activeElement!==$('mode'))$('mode').value=c.recovery_mode;if(document.activeElement!==$('required'))$('required').value=c.required_evidence;if(document.activeElement!==$('delay'))$('delay').value=c.stabilization_delay_ms;if(document.activeElement!==$('limit'))$('limit').value=c.evidence_limit;$('policyResult').textContent='Aktywna rewizja polityki: '+s.policy_revision+'. Oś przechowuje '+s.timeline.length+' z maksymalnie '+c.evidence_limit+' zdarzeń.';$('ritual').textContent='Cykl recovery #'+s.recovery_revision+' · źródło '+(s.source_enabled?'aktywne':'milczy')+' · faza: '+(phaseNames[s.recovery_phase]||s.recovery_phase);$('rooms').innerHTML=s.rooms.length?s.rooms.map(room).join(''):'<p class="empty">Czekam na potwierdzenia…</p>';$('timeline').innerHTML=s.timeline.length?[...s.timeline].reverse().map(e=>'<div class="event"><span class="number">#'+e.number+'</span><div><strong>'+e.title+'</strong><small>'+e.cause+' → '+e.effect+'</small></div></div>').join(''):'<p class="empty">Brak zdarzeń.</p>';$('apply').disabled=s.recovery_phase!=='normal';$('stop').disabled=s.recovery_phase!=='normal'||!s.source_enabled;$('resume').disabled=s.source_enabled||!['waiting','limited'].includes(s.recovery_phase);$('approve').disabled=s.recovery_phase!=='awaiting-approval';$('reject').disabled=s.recovery_phase!=='awaiting-approval';$('retry').disabled=s.recovery_phase!=='rejected'}async function refresh(){try{render(await(await fetch('/api/home',{cache:'no-store'})).json())}catch{}}$('apply').onclick=()=>post('/api/policy',{recovery_mode:$('mode').value,required_evidence:Number($('required').value),stabilization_delay_ms:Number($('delay').value),evidence_limit:Number($('limit').value)});$('stop').onclick=()=>post('/api/source/stop');$('resume').onclick=()=>post('/api/source/resume');$('approve').onclick=()=>post('/api/recovery/approve');$('reject').onclick=()=>post('/api/recovery/reject');$('retry').onclick=()=>post('/api/recovery/retry');$('rollback').onclick=()=>post('/api/rollback');refresh();setInterval(refresh,500);</script></body></html>`

