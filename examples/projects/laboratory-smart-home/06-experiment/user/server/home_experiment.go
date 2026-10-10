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

type experimentConfig struct {
	Threshold          float64   `json:"threshold"`
	CommunicationDelay int       `json:"communication_delay_ms"`
	MaxAge             int       `json:"max_age_ms"`
	Revision           int       `json:"revision"`
	Profile            string    `json:"profile"`
	Hypothesis         string    `json:"hypothesis"`
	ChangedAt          time.Time `json:"changed_at"`
}

type experimentContext struct {
	Threshold          float64 `json:"threshold"`
	CommunicationDelay int     `json:"communication_delay_ms"`
	MaxAge             int     `json:"max_age_ms"`
	ObservationAge     int     `json:"observation_age_ms"`
	Fresh              bool    `json:"fresh"`
	Revision           int     `json:"revision"`
	Profile            string  `json:"profile"`
}

type experimentRoomState struct {
	Room               string    `json:"room"`
	AirQuality         float64   `json:"air_quality"`
	Threshold          float64   `json:"threshold"`
	Decision           string    `json:"decision"`
	RequestedEnabled   bool      `json:"requested_enabled"`
	ActualEnabled      bool      `json:"actual_enabled"`
	Status             string    `json:"status"`
	Cause              string    `json:"cause"`
	Fresh              bool      `json:"fresh"`
	ObservationAge     int       `json:"observation_age_ms"`
	MaxAge             int       `json:"max_age_ms"`
	CommunicationDelay int       `json:"communication_delay_ms"`
	ExperimentRevision int       `json:"experiment_revision"`
	ExperimentProfile  string    `json:"experiment_profile"`
	ObservedAt         time.Time `json:"observed_at"`
	ConfirmedAt        time.Time `json:"confirmed_at"`
}

type experimentSummary struct {
	KnownRooms       int `json:"known_rooms"`
	FreshRooms       int `json:"fresh_rooms"`
	HeldRooms        int `json:"held_rooms"`
	VentilatingRooms int `json:"ventilating_rooms"`
}

type experimentResponse struct {
	Config  experimentConfig      `json:"config"`
	Rooms   []experimentRoomState `json:"rooms"`
	Summary experimentSummary     `json:"summary"`
}

var homeExperiment = struct {
	sync.RWMutex
	config experimentConfig
	rooms  map[string]experimentRoomState
}{
	config: defaultExperimentConfig(1),
	rooms:  make(map[string]experimentRoomState),
}

func defaultExperimentConfig(revision int) experimentConfig {
	return experimentConfig{
		Threshold: 1000, CommunicationDelay: 0, MaxAge: 6000,
		Revision: revision, Profile: "bazowy",
		Hypothesis: "Przebieg odniesienia: świeże dane i próg 1000 ppm.", ChangedAt: time.Now().UTC(),
	}
}

func init() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		log.Printf("[home-experiment] unavailable: %v", err)
		return
	}
	mux := http.NewServeMux()
	mux.HandleFunc("/", serveExperimentPanel)
	mux.HandleFunc("/api/home", serveExperimentState)
	mux.HandleFunc("/api/experiment", configureExperiment)
	mux.HandleFunc("/api/reset", resetExperiment)
	address := fmt.Sprintf("http://%s/", listener.Addr().String())
	log.Printf("PANEL %s", address)
	go func() {
		if err := http.Serve(listener, mux); err != nil && err != http.ErrServerClosed {
			log.Printf("[home-experiment] stopped: %v", err)
		}
	}()
}

// HomeExperiment_evaluate applies the selected communication delay and returns
// one immutable configuration snapshot for a single reading.
func HomeExperiment_evaluate(observedAt any) any {
	homeExperiment.RLock()
	config := homeExperiment.config
	homeExperiment.RUnlock()
	if config.CommunicationDelay > 0 {
		time.Sleep(time.Duration(config.CommunicationDelay) * time.Millisecond)
	}
	observed := decodeObservedAt(observedAt)
	age := int(time.Since(observed).Milliseconds())
	if age < 0 {
		age = 0
	}
	return experimentContext{
		Threshold: config.Threshold, CommunicationDelay: config.CommunicationDelay,
		MaxAge: config.MaxAge, ObservationAge: age, Fresh: age <= config.MaxAge,
		Revision: config.Revision, Profile: config.Profile,
	}
}

func decodeObservedAt(value any) time.Time {
	if observed, ok := value.(time.Time); ok {
		return observed
	}
	payload, err := json.Marshal(value)
	if err == nil {
		var observed time.Time
		if json.Unmarshal(payload, &observed) == nil && !observed.IsZero() {
			return observed
		}
	}
	return time.Now().UTC()
}

// HomeExperiment_publish stores the latest confirmed evidence for one room.
func HomeExperiment_publish(value any) {
	payload, err := json.Marshal(value)
	if err != nil {
		return
	}
	var next experimentRoomState
	if json.Unmarshal(payload, &next) != nil || next.Room == "" {
		return
	}
	homeExperiment.Lock()
	homeExperiment.rooms[next.Room] = next
	homeExperiment.Unlock()
}

func serveExperimentState(response http.ResponseWriter, _ *http.Request) {
	homeExperiment.RLock()
	config := homeExperiment.config
	rooms := make([]experimentRoomState, 0, len(homeExperiment.rooms))
	for _, room := range homeExperiment.rooms {
		rooms = append(rooms, room)
	}
	homeExperiment.RUnlock()
	sort.Slice(rooms, func(left, right int) bool { return experimentRoomRank(rooms[left].Room) < experimentRoomRank(rooms[right].Room) })
	summary := experimentSummary{KnownRooms: len(rooms)}
	for _, room := range rooms {
		if room.Fresh {
			summary.FreshRooms++
		}
		if room.Status == "held-stale-reading" {
			summary.HeldRooms++
		}
		if room.ActualEnabled {
			summary.VentilatingRooms++
		}
	}
	writeJSON(response, http.StatusOK, experimentResponse{Config: config, Rooms: rooms, Summary: summary})
}

func configureExperiment(response http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodPost {
		response.Header().Set("Allow", http.MethodPost)
		http.Error(response, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	var next experimentConfig
	if err := json.NewDecoder(request.Body).Decode(&next); err != nil {
		http.Error(response, "invalid experiment", http.StatusBadRequest)
		return
	}
	if next.Threshold < 400 || next.Threshold > 2000 || next.CommunicationDelay < 0 || next.CommunicationDelay > 2500 || next.MaxAge < 250 || next.MaxAge > 10000 {
		http.Error(response, "experiment values outside safe laboratory limits", http.StatusBadRequest)
		return
	}
	if next.Profile == "" {
		next.Profile = "własny"
	}
	if next.Hypothesis == "" {
		next.Hypothesis = "Brak zapisanej hipotezy."
	}
	homeExperiment.Lock()
	next.Revision = homeExperiment.config.Revision + 1
	next.ChangedAt = time.Now().UTC()
	homeExperiment.config = next
	homeExperiment.Unlock()
	serveExperimentState(response, request)
}

func resetExperiment(response http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodPost {
		response.Header().Set("Allow", http.MethodPost)
		http.Error(response, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	homeExperiment.Lock()
	homeExperiment.config = defaultExperimentConfig(homeExperiment.config.Revision + 1)
	homeExperiment.Unlock()
	serveExperimentState(response, request)
}

func writeJSON(response http.ResponseWriter, status int, value any) {
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	response.WriteHeader(status)
	_ = json.NewEncoder(response).Encode(value)
}

func experimentRoomRank(room string) int {
	switch room {
	case "salon": return 0
	case "kuchnia": return 1
	case "sypialnia": return 2
	default: return 10
	}
}

func serveExperimentPanel(response http.ResponseWriter, request *http.Request) {
	if request.URL.Path != "/" {
		http.NotFound(response, request)
		return
	}
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_, _ = response.Write([]byte(homeExperimentHTML))
}

const homeExperimentHTML = `<!doctype html>
<html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Dom 6 · Eksperymentuj</title>
<style>
:root{color-scheme:dark;font-family:Inter,ui-sans-serif,system-ui,sans-serif;background:#06111d;color:#edf7ff;--cyan:#63d9ff;--mint:#55e3a4;--amber:#ffc568;--red:#ff817d;--panel:#0c2031;--line:#24465f}*{box-sizing:border-box}body{margin:0;min-height:100vh;background:radial-gradient(circle at 8% 0%,#173f62 0,#06111d 38%)}main{width:min(1180px,calc(100% - 30px));margin:auto;padding:42px 0 70px}header{display:flex;justify-content:space-between;align-items:end;gap:20px;margin-bottom:24px}.eyebrow,.label{color:#85cae6;font-size:.74rem;font-weight:800;letter-spacing:.13em;text-transform:uppercase}h1{font-size:clamp(2.4rem,5vw,4.2rem);line-height:1;margin:8px 0}.subtitle{color:#a4bbcf;margin:0}.stage{border:1px solid #34627f;border-radius:999px;padding:10px 15px;background:#0d2639;color:#c9f2ff}.workspace{display:grid;grid-template-columns:360px 1fr;gap:18px}.panel{border:1px solid var(--line);border-radius:22px;background:rgba(9,28,43,.94);box-shadow:0 20px 48px #0003}.controls{padding:23px;align-self:start;position:sticky;top:16px}.controls h2,.evidence h2{margin:7px 0 8px}.hint{color:#91aabd;font-size:.9rem;line-height:1.45}.profiles{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin:18px 0}.profiles button,button.primary,button.reset{border:1px solid #315b76;border-radius:11px;background:#102b40;color:#dff5ff;padding:10px;font:inherit;font-weight:700;cursor:pointer}.profiles button:hover{border-color:var(--cyan)}.field{display:grid;gap:6px;margin:13px 0}.field label{color:#9db3c4;font-size:.82rem}.field input,.field textarea{width:100%;border:1px solid #294c65;border-radius:10px;background:#071a29;color:#fff;padding:10px;font:inherit}.field textarea{min-height:82px;resize:vertical}.actions{display:flex;gap:8px;margin-top:16px}.primary{flex:1!important;background:#087aa2!important;border-color:#24bde9!important}.reset{background:transparent!important}.safe{margin-top:16px;padding:11px;border-left:3px solid var(--mint);background:#0b291f;color:#bfead7;font-size:.82rem;line-height:1.4}.evidence{display:grid;gap:16px}.config{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;padding:18px}.metric{padding:14px;border-radius:14px;background:#0d2639}.metric strong{display:block;margin-top:7px;font-size:1.25rem}.hypothesis{grid-column:1/-1;padding:14px 16px;border-radius:12px;background:#102b3e;color:#d4e8f5}.rooms{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}.room{padding:18px;min-height:290px}.room h3{font-size:1.45rem;margin:6px 0}.reading{font-size:2.25rem;font-weight:800;margin:18px 0}.unit{font-size:.85rem;color:#8ecce4}.fresh{display:inline-block;padding:5px 9px;border-radius:999px;background:#10372c;color:#72efb5;font-size:.75rem;font-weight:800}.stale{background:#41251c;color:#ffbd80}.room dl{display:grid;grid-template-columns:auto 1fr;gap:8px 10px;margin:18px 0 0;font-size:.87rem}.room dt{color:#809db2}.room dd{margin:0;text-align:right;font-weight:700;overflow-wrap:anywhere}.on{color:var(--mint)}.off{color:var(--amber)}.held{color:#ffae75}.explanation{padding:19px 21px;line-height:1.5}.explanation strong{color:var(--cyan)}footer{margin-top:20px;color:#708ca1;font-size:.84rem}@media(max-width:950px){.workspace{grid-template-columns:1fr}.controls{position:static}.rooms{grid-template-columns:1fr}.config{grid-template-columns:1fr 1fr}}@media(max-width:560px){header{align-items:start;flex-direction:column}.profiles,.config{grid-template-columns:1fr}.actions{flex-direction:column}}
</style></head><body><main>
<header><div><div class="eyebrow">Laboratorium Junexis · Dom 6</div><h1>Eksperymentuj</h1><p class="subtitle">Zmień jedną decyzję. Przewidź skutek. Odczytaj dowód.</p></div><div class="stage">system działa · rewizja <span id="revision">1</span></div></header>
<section class="workspace"><aside class="panel controls"><div class="label">Sterowanie eksperymentem</div><h2>Najpierw hipoteza</h2><p class="hint">Wybierz przygotowany profil albo zmień jedną wartość. System ogranicza zakres do bezpiecznej symulacji.</p>
<div class="profiles"><button data-profile="bazowy">Bazowy</button><button data-profile="wyższy-próg">Próg 1400</button><button data-profile="wolniej">Opóźnienie</button><button data-profile="świeżość">Granica świeżości</button></div>
<div class="field"><label for="threshold">Próg jakości powietrza · ppm</label><input id="threshold" type="number" min="400" max="2000" step="50"></div>
<div class="field"><label for="delay">Opóźnienie komunikacji · ms</label><input id="delay" type="number" min="0" max="2500" step="100"></div>
<div class="field"><label for="maxAge">Maksymalny wiek pomiaru · ms</label><input id="maxAge" type="number" min="250" max="10000" step="250"></div>
<div class="field"><label for="hypothesis">Moja hipoteza</label><textarea id="hypothesis" placeholder="Spodziewam się, że..."></textarea></div>
<div class="actions"><button class="primary" id="apply">Uruchom eksperyment</button><button class="reset" id="reset">Reset</button></div>
<div class="safe">Nie zatrzymujemy czujników. Gdy pomiar jest zbyt stary, wykonawca zachowuje ostatni potwierdzony stan pokoju.</div></aside>
<div class="evidence"><section class="panel config" aria-label="Efektywna konfiguracja"><article class="metric"><div class="label">Profil</div><strong id="profile">bazowy</strong></article><article class="metric"><div class="label">Próg</div><strong><span id="activeThreshold">1000</span> ppm</strong></article><article class="metric"><div class="label">Opóźnienie</div><strong><span id="activeDelay">0</span> ms</strong></article><article class="metric"><div class="label">Budżet wieku</div><strong><span id="activeMaxAge">6000</span> ms</strong></article><div class="hypothesis"><span class="label">Hipoteza rewizji</span><div id="activeHypothesis">Przebieg odniesienia.</div></div></section>
<section class="rooms" aria-label="Dowody z pomieszczeń">
<article class="panel room" data-room="salon"><div class="label">Pomieszczenie</div><h3>Salon</h3><span class="fresh">oczekiwanie</span><div class="reading"><span class="ppm">—</span> <span class="unit">ppm</span></div><dl><dt>Rewizja / profil</dt><dd class="room-revision">—</dd><dt>Wiek / budżet</dt><dd class="age">—</dd><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wykonanie</dt><dd class="execution">—</dd><dt>Wentylacja</dt><dd class="ventilation">—</dd></dl></article>
<article class="panel room" data-room="kuchnia"><div class="label">Pomieszczenie</div><h3>Kuchnia</h3><span class="fresh">oczekiwanie</span><div class="reading"><span class="ppm">—</span> <span class="unit">ppm</span></div><dl><dt>Rewizja / profil</dt><dd class="room-revision">—</dd><dt>Wiek / budżet</dt><dd class="age">—</dd><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wykonanie</dt><dd class="execution">—</dd><dt>Wentylacja</dt><dd class="ventilation">—</dd></dl></article>
<article class="panel room" data-room="sypialnia"><div class="label">Pomieszczenie</div><h3>Sypialnia</h3><span class="fresh">oczekiwanie</span><div class="reading"><span class="ppm">—</span> <span class="unit">ppm</span></div><dl><dt>Rewizja / profil</dt><dd class="room-revision">—</dd><dt>Wiek / budżet</dt><dd class="age">—</dd><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wykonanie</dt><dd class="execution">—</dd><dt>Wentylacja</dt><dd class="ventilation">—</dd></dl></article>
</section><section class="panel explanation"><div class="label">Wyjaśnienie przebiegu</div><p id="explanation"><strong>Profil bazowy:</strong> dane mieszczą się w budżecie świeżości, dlatego reguła może sterować wentylacją.</p></section></div></section>
<footer>Panel pokazuje konfigurację efektywną oraz potwierdzenia wykonawcy. Zmiana formularza nie działa, dopóki nie wybierzesz „Uruchom eksperyment”.</footer>
</main><script>
const $=id=>document.getElementById(id);const cards=new Map([...document.querySelectorAll('[data-room]')].map(card=>[card.dataset.room,card]));let selectedProfile='własny';
const profiles={bazowy:{threshold:1000,delay:0,maxAge:6000,hypothesis:'Przebieg odniesienia: świeże dane i próg 1000 ppm.'},'wyższy-próg':{threshold:1400,delay:0,maxAge:6000,hypothesis:'1250 ppm nie przekroczy progu 1400, ale 1650 ppm nadal go przekroczy.'},wolniej:{threshold:1000,delay:750,maxAge:6000,hypothesis:'Decyzje pozostaną takie same, lecz wiek pomiarów wzrośnie.'},'świeżość':{threshold:1000,delay:1500,maxAge:500,hypothesis:'Wiek przekroczy budżet, więc nowe polecenia zostaną wstrzymane.'}};
function selectProfile(name){const p=profiles[name];selectedProfile=name;$('threshold').value=p.threshold;$('delay').value=p.delay;$('maxAge').value=p.maxAge;$('hypothesis').value=p.hypothesis}document.querySelectorAll('[data-profile]').forEach(button=>button.onclick=()=>selectProfile(button.dataset.profile));['threshold','delay','maxAge','hypothesis'].forEach(id=>$(id).addEventListener('input',()=>selectedProfile='własny'));
async function post(path,body){const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:body?JSON.stringify(body):'{}'});if(!response.ok)throw new Error(await response.text());return response.json()}
$('apply').onclick=async()=>{try{await post('/api/experiment',{threshold:Number($('threshold').value),communication_delay_ms:Number($('delay').value),max_age_ms:Number($('maxAge').value),profile:selectedProfile,hypothesis:$('hypothesis').value});await refresh()}catch(error){alert(error.message)}};$('reset').onclick=async()=>{await post('/api/reset');selectProfile('bazowy');await refresh()};
function renderRoom(state){const card=cards.get(state.room);if(!card)return;const badge=card.querySelector('.fresh');badge.textContent=state.fresh?'świeży':'poza budżetem';badge.className=state.fresh?'fresh':'fresh stale';card.querySelector('.ppm').textContent=state.air_quality.toFixed(0);card.querySelector('.room-revision').textContent=state.experiment_revision+' · '+state.experiment_profile;card.querySelector('.age').textContent=state.observation_age_ms+' / '+state.max_age_ms+' ms';card.querySelector('.decision').textContent=state.decision;const execution=card.querySelector('.execution');execution.textContent=state.status;execution.className='execution '+(state.status==='held-stale-reading'?'held':'');const ventilation=card.querySelector('.ventilation');ventilation.textContent=state.actual_enabled?'WŁĄCZONA':'WYŁĄCZONA';ventilation.className='ventilation '+(state.actual_enabled?'on':'off')}
function explain(config,summary){if(summary.held_rooms>0)return '<strong>Granica świeżości:</strong> wiek pomiaru przekroczył budżet. Nowe polecenie zostało wstrzymane, a wykonawca zachował ostatni stan pokoju.';if(config.communication_delay_ms>0)return '<strong>Wolniejsza komunikacja:</strong> pomiary docierają później, ale nadal mieszczą się w budżecie i przechodzą przez tę samą regułę.';if(config.threshold>1000)return '<strong>Wyższy próg:</strong> zmieniła się reguła decyzji, nie pomiary ani komunikacja. Porównaj 1250 i 1650 ppm.';return '<strong>Profil bazowy:</strong> dane mieszczą się w budżecie świeżości, dlatego reguła może sterować wentylacją.'}
async function refresh(){try{const state=await(await fetch('/api/home',{cache:'no-store'})).json();$('revision').textContent=state.config.revision;$('profile').textContent=state.config.profile;$('activeThreshold').textContent=state.config.threshold.toFixed(0);$('activeDelay').textContent=state.config.communication_delay_ms;$('activeMaxAge').textContent=state.config.max_age_ms;$('activeHypothesis').textContent=state.config.hypothesis;$('explanation').innerHTML=explain(state.config,state.summary);state.rooms.forEach(renderRoom);if(document.activeElement.tagName!=='INPUT'&&document.activeElement.tagName!=='TEXTAREA'&&selectedProfile!=='własny'){selectProfile(state.config.profile in profiles?state.config.profile:'bazowy')}}catch(_){$('explanation').textContent='Brak połączenia z wykonywanym systemem.'}}
selectProfile('bazowy');refresh();setInterval(refresh,500);
</script></body></html>`
