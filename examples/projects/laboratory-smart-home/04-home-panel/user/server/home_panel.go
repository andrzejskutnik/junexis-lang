package user

import (
	"encoding/json"
	"fmt"
	"log"
	"net"
	"net/http"
	"sync"
	"time"
)

type homePanelState struct {
	Room             string    `json:"room"`
	AirQuality       float64   `json:"air_quality"`
	Threshold        float64   `json:"threshold"`
	Decision         string    `json:"decision"`
	RequestedEnabled bool      `json:"requested_enabled"`
	ActualEnabled    bool      `json:"actual_enabled"`
	Status           string    `json:"status"`
	Cause            string    `json:"cause"`
	ConfirmedAt      time.Time `json:"confirmed_at"`
}

var homePanel = struct {
	sync.RWMutex
	state homePanelState
}{state: homePanelState{Status: "waiting"}}

func init() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		log.Printf("[home-panel] unavailable: %v", err)
		return
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/", serveHomePanel)
	mux.HandleFunc("/api/home", serveHomeState)
	address := fmt.Sprintf("http://%s/", listener.Addr().String())
	log.Printf("PANEL %s", address)
	go func() {
		if err := http.Serve(listener, mux); err != nil && err != http.ErrServerClosed {
			log.Printf("[home-panel] stopped: %v", err)
		}
	}()
}

// HomePanel_publish is the developer-owned implementation of the Junexis extern boundary.
func HomePanel_publish(value any) {
	payload, err := json.Marshal(value)
	if err != nil {
		return
	}
	var next homePanelState
	if err := json.Unmarshal(payload, &next); err != nil {
		return
	}
	homePanel.Lock()
	homePanel.state = next
	homePanel.Unlock()
}

func serveHomeState(response http.ResponseWriter, _ *http.Request) {
	homePanel.RLock()
	state := homePanel.state
	homePanel.RUnlock()
	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_ = json.NewEncoder(response).Encode(state)
}

func serveHomePanel(response http.ResponseWriter, request *http.Request) {
	if request.URL.Path != "/" {
		http.NotFound(response, request)
		return
	}
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_, _ = response.Write([]byte(homePanelHTML))
}

const homePanelHTML = `<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Dom Junexis</title>
  <style>
    :root { color-scheme: dark; font-family: Inter, ui-sans-serif, system-ui, sans-serif; background: #07111f; color: #edf6ff; }
    * { box-sizing: border-box; }
    body { margin: 0; min-height: 100vh; background: radial-gradient(circle at 15% 0%, #12375a 0, #07111f 42%); }
    main { width: min(1060px, calc(100% - 32px)); margin: 0 auto; padding: 54px 0 72px; }
    header { display: flex; align-items: end; justify-content: space-between; gap: 24px; margin-bottom: 32px; }
    .eyebrow { color: #71d8ff; font-size: .78rem; font-weight: 800; letter-spacing: .16em; text-transform: uppercase; }
    h1 { margin: 8px 0 6px; font-size: clamp(2rem, 5vw, 4rem); line-height: .98; }
    .subtitle { margin: 0; color: #a8bdd2; }
    .live { display: inline-flex; align-items: center; gap: 9px; padding: 9px 14px; border: 1px solid #28506f; border-radius: 999px; color: #bfefff; background: #0d2235; }
    .dot { width: 9px; height: 9px; border-radius: 50%; background: #38e59d; box-shadow: 0 0 16px #38e59d; }
    .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    .card { min-height: 156px; padding: 22px; border: 1px solid #1d3b54; border-radius: 20px; background: rgba(10, 28, 44, .86); box-shadow: 0 20px 45px rgba(0,0,0,.2); }
    .wide { grid-column: span 2; }
    .label { color: #86a3ba; font-size: .76rem; font-weight: 750; letter-spacing: .1em; text-transform: uppercase; }
    .value { margin-top: 16px; font-size: clamp(1.45rem, 3vw, 2.35rem); font-weight: 760; overflow-wrap: anywhere; }
    .unit { color: #8fcde7; font-size: 1rem; font-weight: 600; }
    .reason { color: #d7e8f5; font-size: 1.2rem; line-height: 1.45; }
    .on { color: #51f0aa; } .off { color: #ffcf70; } .waiting { color: #91a9bc; }
    footer { margin-top: 22px; color: #7892a8; font-size: .88rem; }
    @media (max-width: 760px) { .grid { grid-template-columns: 1fr; } .wide { grid-column: span 1; } header { align-items: start; flex-direction: column; } }
  </style>
</head>
<body>
<main>
  <header>
    <div><div class="eyebrow">Laboratorium Junexis · Dom 4</div><h1>Salon</h1><p class="subtitle">Od pomiaru do potwierdzonego działania</p></div>
    <div class="live"><span class="dot"></span><span id="connection">system działa</span></div>
  </header>
  <section class="grid">
    <article class="card"><div class="label">Pomiar</div><div class="value"><span id="air">—</span> <span class="unit">ppm</span></div></article>
    <article class="card"><div class="label">Próg</div><div class="value"><span id="threshold">—</span> <span class="unit">ppm</span></div></article>
    <article class="card"><div class="label">Wentylacja</div><div id="ventilation" class="value waiting">oczekiwanie</div></article>
    <article class="card"><div class="label">Decyzja</div><div id="decision" class="value">—</div></article>
    <article class="card"><div class="label">Wykonanie</div><div id="execution" class="value">—</div></article>
    <article class="card wide"><div class="label">Dlaczego?</div><div id="cause" class="value reason">Czekam na pierwszy potwierdzony pomiar.</div></article>
    <article class="card"><div class="label">Potwierdzono</div><div id="time" class="value">—</div></article>
  </section>
  <footer>Panel wyświetla stan opublikowany przez VentilationActuator. Nie odgaduje wyniku na podstawie decyzji.</footer>
</main>
<script>
const byId = id => document.getElementById(id);
async function refresh() {
  try {
    const response = await fetch('/api/home', { cache: 'no-store' });
    const state = await response.json();
    byId('connection').textContent = state.status === 'waiting' ? 'czekam na dane' : 'system działa';
    if (state.status === 'waiting') return;
    byId('air').textContent = state.air_quality.toFixed(0);
    byId('threshold').textContent = state.threshold.toFixed(0);
    byId('decision').textContent = state.decision;
    byId('execution').textContent = state.status + ' · żądano ' + (state.requested_enabled ? 'ON' : 'OFF');
    const ventilation = byId('ventilation');
    ventilation.textContent = state.actual_enabled ? 'WŁĄCZONA' : 'WYŁĄCZONA';
    ventilation.className = 'value ' + (state.actual_enabled ? 'on' : 'off');
    byId('cause').textContent = state.cause;
    byId('time').textContent = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date(state.confirmed_at));
  } catch (_) { byId('connection').textContent = 'brak połączenia'; }
}
refresh(); setInterval(refresh, 500);
</script>
</body>
</html>`
