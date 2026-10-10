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

const silenceTimeout = 4500 * time.Millisecond

type failureSignal struct {
	Kind           string    `json:"kind"`
	Room           string    `json:"room"`
	AirQuality     float64   `json:"air_quality"`
	SourceStatus   string    `json:"source_status"`
	SilenceAge     int       `json:"silence_age_ms"`
	SilenceTimeout int       `json:"silence_timeout_ms"`
	ObservedAt     time.Time `json:"observed_at"`
}

type failureRoomState struct {
	Room             string    `json:"room"`
	AirQuality       float64   `json:"air_quality"`
	Decision         string    `json:"decision"`
	RequestedEnabled bool      `json:"requested_enabled"`
	ActualEnabled    bool      `json:"actual_enabled"`
	Status           string    `json:"status"`
	Cause            string    `json:"cause"`
	SourceStatus     string    `json:"source_status"`
	SilenceAge       int       `json:"silence_age_ms"`
	SilenceTimeout   int       `json:"silence_timeout_ms"`
	ConfirmedAt      time.Time `json:"confirmed_at"`
}

type failureEvent struct {
	Kind       string    `json:"kind"`
	Title      string    `json:"title"`
	Cause      string    `json:"cause"`
	Effect     string    `json:"effect"`
	OccurredAt time.Time `json:"occurred_at"`
}

type failureResponse struct {
	SourceEnabled bool               `json:"source_enabled"`
	Mode          string             `json:"mode"`
	LastSalonAt   time.Time          `json:"last_salon_at"`
	SilenceAge    int                `json:"silence_age_ms"`
	SilenceTimeout int               `json:"silence_timeout_ms"`
	Rooms         []failureRoomState `json:"rooms"`
	Timeline      []failureEvent     `json:"timeline"`
}

var firstSilence = struct {
	sync.RWMutex
	sourceEnabled bool
	mode          string
	lastSalonAt   time.Time
	lastSalonValue float64
	rooms         map[string]failureRoomState
	timeline      []failureEvent
}{
	sourceEnabled: true,
	mode:          "normal",
	lastSalonAt:   time.Now().UTC(),
	rooms:         make(map[string]failureRoomState),
}

func init() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		log.Printf("[first-silence] unavailable: %v", err)
		return
	}
	mux := http.NewServeMux()
	mux.HandleFunc("/", serveFailurePanel)
	mux.HandleFunc("/api/home", serveFailureState)
	mux.HandleFunc("/api/source/stop", stopSalonSource)
	mux.HandleFunc("/api/source/resume", resumeSalonSource)
	mux.HandleFunc("/api/reset", resetFirstSilence)
	address := fmt.Sprintf("http://%s/", listener.Addr().String())
	log.Printf("PANEL %s", address)
	go func() {
		if err := http.Serve(listener, mux); err != nil && err != http.ErrServerClosed {
			log.Printf("[first-silence] stopped: %v", err)
		}
	}()
}

func HomeFailure_salon_available() bool {
	firstSilence.RLock()
	defer firstSilence.RUnlock()
	return firstSilence.sourceEnabled
}

func HomeFailure_record_salon(observedAt any, airQuality float64) {
	observed := decodeFailureTime(observedAt)
	firstSilence.Lock()
	firstSilence.lastSalonAt = observed
	firstSilence.lastSalonValue = airQuality
	firstSilence.Unlock()
}

func HomeFailure_health() any {
	firstSilence.Lock()
	age := int(time.Since(firstSilence.lastSalonAt).Milliseconds())
	if age < 0 { age = 0 }
	status := "online"
	if !firstSilence.sourceEnabled && age <= int(silenceTimeout/time.Millisecond) {
		status = "waiting-for-timeout"
	}
	if !firstSilence.sourceEnabled && age > int(silenceTimeout/time.Millisecond) {
		status = "silent"
	}
	signal := failureSignal{
		Kind: "source-health", Room: "salon", AirQuality: firstSilence.lastSalonValue,
		SourceStatus: status, SilenceAge: age,
		SilenceTimeout: int(silenceTimeout / time.Millisecond), ObservedAt: firstSilence.lastSalonAt,
	}
	firstSilence.Unlock()
	return signal
}

func HomeFailure_publish(value any) {
	payload, err := json.Marshal(value)
	if err != nil { return }
	var next failureRoomState
	if json.Unmarshal(payload, &next) != nil || next.Room == "" { return }

	firstSilence.Lock()
	previousMode := firstSilence.mode
	firstSilence.rooms[next.Room] = next
	if next.Room == "salon" && next.Status == "limited-source-silent" {
		firstSilence.mode = "limited"
		if previousMode != "limited" {
			appendFailureEventLocked("detection", "Monitor wykrył ciszę salonu", "brak pomiaru przez 4500 ms", "stan ograniczony")
		}
	}
	if next.Room == "salon" && next.Status == "applied" && next.SourceStatus == "online" {
		firstSilence.mode = "normal"
		if previousMode == "limited" || previousMode == "resuming" {
			appendFailureEventLocked("restoration", "Świeży pomiar salonu wrócił", "wznowione źródło dostarczyło dane", "normalne sterowanie")
		}
	}
	firstSilence.Unlock()
}

func decodeFailureTime(value any) time.Time {
	if observed, ok := value.(time.Time); ok { return observed }
	payload, err := json.Marshal(value)
	if err == nil {
		var observed time.Time
		if json.Unmarshal(payload, &observed) == nil && !observed.IsZero() { return observed }
	}
	return time.Now().UTC()
}

func stopSalonSource(response http.ResponseWriter, request *http.Request) {
	if !requirePost(response, request) { return }
	firstSilence.Lock()
	if firstSilence.sourceEnabled {
		firstSilence.sourceEnabled = false
		firstSilence.mode = "waiting"
		appendFailureEventLocked("control", "Użytkownik zatrzymał źródło salonu", "świadome polecenie laboratorium", "kanał pomiarów milczy")
	}
	firstSilence.Unlock()
	serveFailureState(response, request)
}

func resumeSalonSource(response http.ResponseWriter, request *http.Request) {
	if !requirePost(response, request) { return }
	firstSilence.Lock()
	if !firstSilence.sourceEnabled {
		firstSilence.sourceEnabled = true
		firstSilence.mode = "resuming"
		appendFailureEventLocked("control", "Użytkownik wznowił źródło salonu", "świadome polecenie laboratorium", "oczekiwanie na świeży pomiar")
	}
	firstSilence.Unlock()
	serveFailureState(response, request)
}

func resetFirstSilence(response http.ResponseWriter, request *http.Request) {
	if !requirePost(response, request) { return }
	firstSilence.Lock()
	firstSilence.sourceEnabled = true
	firstSilence.mode = "normal"
	firstSilence.lastSalonAt = time.Now().UTC()
	firstSilence.timeline = nil
	appendFailureEventLocked("reset", "Przywrócono punkt odniesienia", "reset laboratorium", "źródło salonu działa")
	firstSilence.Unlock()
	serveFailureState(response, request)
}

func requirePost(response http.ResponseWriter, request *http.Request) bool {
	if request.Method == http.MethodPost { return true }
	response.Header().Set("Allow", http.MethodPost)
	http.Error(response, "method not allowed", http.StatusMethodNotAllowed)
	return false
}

func appendFailureEventLocked(kind, title, cause, effect string) {
	firstSilence.timeline = append(firstSilence.timeline, failureEvent{
		Kind: kind, Title: title, Cause: cause, Effect: effect, OccurredAt: time.Now().UTC(),
	})
	if len(firstSilence.timeline) > 8 {
		firstSilence.timeline = append([]failureEvent(nil), firstSilence.timeline[len(firstSilence.timeline)-8:]...)
	}
}

func serveFailureState(response http.ResponseWriter, _ *http.Request) {
	firstSilence.RLock()
	rooms := make([]failureRoomState, 0, len(firstSilence.rooms))
	for _, room := range firstSilence.rooms { rooms = append(rooms, room) }
	sort.Slice(rooms, func(left, right int) bool { return failureRoomRank(rooms[left].Room) < failureRoomRank(rooms[right].Room) })
	age := int(time.Since(firstSilence.lastSalonAt).Milliseconds())
	if age < 0 { age = 0 }
	result := failureResponse{
		SourceEnabled: firstSilence.sourceEnabled, Mode: firstSilence.mode,
		LastSalonAt: firstSilence.lastSalonAt, SilenceAge: age,
		SilenceTimeout: int(silenceTimeout / time.Millisecond), Rooms: rooms,
		Timeline: append([]failureEvent{}, firstSilence.timeline...),
	}
	firstSilence.RUnlock()
	writeFailureJSON(response, result)
}

func failureRoomRank(room string) int {
	switch room { case "salon": return 0; case "kuchnia": return 1; case "sypialnia": return 2; default: return 10 }
}

func writeFailureJSON(response http.ResponseWriter, value any) {
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_ = json.NewEncoder(response).Encode(value)
}

func serveFailurePanel(response http.ResponseWriter, request *http.Request) {
	if request.URL.Path != "/" { http.NotFound(response, request); return }
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_, _ = response.Write([]byte(firstSilenceHTML))
}

const firstSilenceHTML = `<!doctype html><html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Dom 7 · Pierwsza cisza</title>
<style>:root{color-scheme:dark;font-family:Inter,ui-sans-serif,system-ui,sans-serif;background:#07101c;color:#eef7ff;--cyan:#69dafa;--mint:#55e1a4;--amber:#ffc66d;--red:#ff887f;--panel:#0c2030;--line:#24475f}*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(circle at 10% 0,#173e61 0,#07101c 40%)}main{width:min(1140px,calc(100% - 30px));margin:auto;padding:44px 0 70px}.eyebrow,.label{color:#89cee8;font-size:.73rem;font-weight:800;letter-spacing:.13em;text-transform:uppercase}h1{font-size:clamp(2.35rem,5vw,4.1rem);line-height:1;margin:8px 0}.subtitle{margin:0;color:#a3bbce}header{display:flex;justify-content:space-between;align-items:end;gap:20px;margin-bottom:26px}.mode{padding:10px 15px;border:1px solid #32607d;border-radius:999px;background:#0e2739}.mode.limited{border-color:#925744;background:#352017;color:#ffd1a5}.panel{border:1px solid var(--line);border-radius:21px;background:rgba(10,28,43,.94);box-shadow:0 20px 45px #0003}.story{display:grid;grid-template-columns:1.2fr .8fr;gap:16px}.control{padding:23px}.control h2{margin:7px 0}.hint{color:#93aabd;line-height:1.5}.actions{display:flex;gap:9px;margin-top:18px}.actions button{border:1px solid #36617c;border-radius:11px;padding:11px 15px;background:#112b3f;color:#e7f8ff;font:inherit;font-weight:750;cursor:pointer}.actions .stop{background:#8a392f;border-color:#e26b5e}.actions .resume{background:#087b60;border-color:#34c99b}.actions button:disabled{opacity:.4;cursor:not-allowed}.clock{padding:23px}.clock-value{font-size:2.8rem;font-weight:820;margin:14px 0 5px}.clock-bar{height:9px;border-radius:999px;background:#173146;overflow:hidden}.clock-bar span{display:block;height:100%;width:0;background:var(--mint);transition:width .3s}.clock-meta{display:flex;justify-content:space-between;color:#8ea8bb;font-size:.8rem;margin-top:8px}.rooms{display:grid;grid-template-columns:repeat(3,1fr);gap:13px;margin-top:16px}.room{padding:20px;min-height:300px}.room h3{font-size:1.55rem;margin:6px 0}.badge{display:inline-block;padding:5px 9px;border-radius:999px;background:#10372c;color:#76efb8;font-size:.74rem;font-weight:800}.badge.silent{background:#49261d;color:#ffc08b}.reading{font-size:2.35rem;font-weight:820;margin:20px 0}.unit{font-size:.85rem;color:#8ccce5}.room dl{display:grid;grid-template-columns:auto 1fr;gap:9px 10px;font-size:.87rem}.room dt{color:#819eb3}.room dd{margin:0;text-align:right;font-weight:700;overflow-wrap:anywhere}.on{color:var(--mint)}.off{color:var(--amber)}.held{color:#ffad7a}.timeline{margin-top:16px;padding:22px}.timeline h2{margin:7px 0 15px}.events{display:grid;gap:10px}.event{display:grid;grid-template-columns:105px 1fr;gap:13px;padding:13px;border-radius:13px;background:#0e2739}.kind{color:var(--cyan);font-size:.72rem;font-weight:800;text-transform:uppercase}.chain{color:#91a9ba;font-size:.84rem;margin-top:5px}.empty{color:#7d97aa;padding:12px 0}footer{margin-top:20px;color:#708da2;font-size:.85rem;line-height:1.5}@media(max-width:850px){.story{grid-template-columns:1fr}.rooms{grid-template-columns:1fr}}@media(max-width:560px){header{align-items:start;flex-direction:column}.actions{flex-direction:column}.event{grid-template-columns:1fr}}</style></head><body><main>
<header><div><div class="eyebrow">Laboratorium Junexis · Dom 7</div><h1>Pierwsza cisza</h1><p class="subtitle">Brak wiadomości też jest informacją — ale dopiero po upływie czasu.</p></div><div id="mode" class="mode">normalne działanie</div></header>
<section class="story"><article class="panel control"><div class="label">Kontrolowany scenariusz</div><h2>Zatrzymaj tylko salon</h2><p class="hint">Kuchnia i sypialnia nadal publikują pomiary. Salon nie wysyła zastępczego komunikatu „offline”; jego kanał naprawdę milczy.</p><div class="actions"><button id="stop" class="stop">Zatrzymaj źródło salonu</button><button id="resume" class="resume">Wznów źródło</button><button id="reset">Reset</button></div></article>
<article class="panel clock"><div class="label">Czas od ostatniego pomiaru salonu</div><div class="clock-value"><span id="age">0</span> <span class="unit">ms</span></div><div class="clock-bar"><span id="bar"></span></div><div class="clock-meta"><span>ostatni pomiar</span><span>limit 4500 ms</span></div></article></section>
<section class="rooms" aria-label="Stan pomieszczeń"><article class="panel room" data-room="salon"><div class="label">Pomieszczenie</div><h3>Salon</h3><span class="badge">oczekiwanie</span><div class="reading"><span class="ppm">—</span> <span class="unit">ppm</span></div><dl><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wykonanie</dt><dd class="execution">—</dd><dt>Wentylacja</dt><dd class="ventilation">—</dd><dt>Dlaczego?</dt><dd class="cause">—</dd></dl></article>
<article class="panel room" data-room="kuchnia"><div class="label">Pomieszczenie</div><h3>Kuchnia</h3><span class="badge">oczekiwanie</span><div class="reading"><span class="ppm">—</span> <span class="unit">ppm</span></div><dl><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wykonanie</dt><dd class="execution">—</dd><dt>Wentylacja</dt><dd class="ventilation">—</dd><dt>Dlaczego?</dt><dd class="cause">—</dd></dl></article>
<article class="panel room" data-room="sypialnia"><div class="label">Pomieszczenie</div><h3>Sypialnia</h3><span class="badge">oczekiwanie</span><div class="reading"><span class="ppm">—</span> <span class="unit">ppm</span></div><dl><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wykonanie</dt><dd class="execution">—</dd><dt>Wentylacja</dt><dd class="ventilation">—</dd><dt>Dlaczego?</dt><dd class="cause">—</dd></dl></article></section>
<section class="panel timeline"><div class="label">Oś przyczyn</div><h2>Co wydarzyło się naprawdę?</h2><div id="events" class="events"><div class="empty">Zatrzymaj źródło, aby rozpocząć kontrolowany scenariusz.</div></div></section>
<footer>Stan ograniczony nie oznacza awarii całego domu. Pozostałe pokoje działają, a wykonawca salonu zachowuje ostatni potwierdzony stan do czasu otrzymania świeżego pomiaru.</footer>
</main><script>const $=id=>document.getElementById(id);const cards=new Map([...document.querySelectorAll('[data-room]')].map(card=>[card.dataset.room,card]));async function command(path){const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});if(!response.ok)throw new Error(await response.text());await refresh()}$('stop').onclick=()=>command('/api/source/stop');$('resume').onclick=()=>command('/api/source/resume');$('reset').onclick=()=>command('/api/reset');function renderRoom(room){const card=cards.get(room.room);if(!card)return;const badge=card.querySelector('.badge');badge.textContent=room.source_status==='silent'?'źródło milczy':'dane potwierdzone';badge.className='badge '+(room.source_status==='silent'?'silent':'');card.querySelector('.ppm').textContent=room.air_quality.toFixed(0);card.querySelector('.decision').textContent=room.decision;const execution=card.querySelector('.execution');execution.textContent=room.status;execution.className='execution '+(room.status==='limited-source-silent'?'held':'');const ventilation=card.querySelector('.ventilation');ventilation.textContent=room.actual_enabled?'WŁĄCZONA':'WYŁĄCZONA';ventilation.className='ventilation '+(room.actual_enabled?'on':'off');card.querySelector('.cause').textContent=room.cause}function modeName(mode){return {normal:'normalne działanie',waiting:'czekam na limit ciszy',limited:'stan ograniczony',resuming:'czekam na świeży pomiar'}[mode]||mode}async function refresh(){try{const state=await(await fetch('/api/home',{cache:'no-store'})).json();$('mode').textContent=modeName(state.mode);$('mode').className='mode '+(state.mode==='limited'?'limited':'');$('age').textContent=state.silence_age_ms;$('bar').style.width=Math.min(100,state.silence_age_ms/state.silence_timeout_ms*100)+'%';$('bar').style.background=state.silence_age_ms>state.silence_timeout_ms?'var(--red)':'var(--mint)';$('stop').disabled=!state.source_enabled;$('resume').disabled=state.source_enabled;state.rooms.forEach(renderRoom);$('events').innerHTML=state.timeline.length?state.timeline.slice().reverse().map(event=>'<article class="event"><div class="kind">'+event.kind+'</div><div><strong>'+event.title+'</strong><div class="chain">przyczyna: '+event.cause+' → skutek: '+event.effect+'</div></div></article>').join(''):'<div class="empty">Zatrzymaj źródło, aby rozpocząć kontrolowany scenariusz.</div>'}catch(_){$('mode').textContent='brak połączenia'}}refresh();setInterval(refresh,400);</script></body></html>`
