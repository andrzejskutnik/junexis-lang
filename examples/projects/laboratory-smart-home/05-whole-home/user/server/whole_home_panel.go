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

type roomPanelState struct {
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

type wholeHomeSummary struct {
	KnownRooms        int     `json:"known_rooms"`
	VentilatingRooms  int     `json:"ventilating_rooms"`
	HighestAirQuality float64 `json:"highest_air_quality"`
	HighestRoom       string  `json:"highest_room"`
	Status            string  `json:"status"`
}

type wholeHomeResponse struct {
	Rooms   []roomPanelState `json:"rooms"`
	Summary wholeHomeSummary `json:"summary"`
}

var wholeHomePanel = struct {
	sync.RWMutex
	rooms map[string]roomPanelState
}{rooms: make(map[string]roomPanelState)}

func init() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		log.Printf("[whole-home-panel] unavailable: %v", err)
		return
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/", serveWholeHomePanel)
	mux.HandleFunc("/api/home", serveWholeHomeState)
	address := fmt.Sprintf("http://%s/", listener.Addr().String())
	log.Printf("PANEL %s", address)
	go func() {
		if err := http.Serve(listener, mux); err != nil && err != http.ErrServerClosed {
			log.Printf("[whole-home-panel] stopped: %v", err)
		}
	}()
}

// WholeHomePanel_publish is the developer-owned implementation of the Junexis extern boundary.
func WholeHomePanel_publish(value any) {
	payload, err := json.Marshal(value)
	if err != nil {
		return
	}
	var next roomPanelState
	if err := json.Unmarshal(payload, &next); err != nil || next.Room == "" {
		return
	}
	wholeHomePanel.Lock()
	wholeHomePanel.rooms[next.Room] = next
	wholeHomePanel.Unlock()
}

func serveWholeHomeState(response http.ResponseWriter, _ *http.Request) {
	wholeHomePanel.RLock()
	rooms := make([]roomPanelState, 0, len(wholeHomePanel.rooms))
	for _, room := range wholeHomePanel.rooms {
		rooms = append(rooms, room)
	}
	wholeHomePanel.RUnlock()

	sort.Slice(rooms, func(left, right int) bool { return roomRank(rooms[left].Room) < roomRank(rooms[right].Room) })
	summary := wholeHomeSummary{KnownRooms: len(rooms), Status: "waiting"}
	if len(rooms) > 0 {
		summary.Status = "active"
	}
	for _, room := range rooms {
		if room.ActualEnabled {
			summary.VentilatingRooms++
		}
		if summary.HighestRoom == "" || room.AirQuality > summary.HighestAirQuality {
			summary.HighestAirQuality = room.AirQuality
			summary.HighestRoom = room.Room
		}
	}

	response.Header().Set("Content-Type", "application/json; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_ = json.NewEncoder(response).Encode(wholeHomeResponse{Rooms: rooms, Summary: summary})
}

func roomRank(room string) int {
	switch room {
	case "salon":
		return 0
	case "kuchnia":
		return 1
	case "sypialnia":
		return 2
	default:
		return 10
	}
}

func serveWholeHomePanel(response http.ResponseWriter, request *http.Request) {
	if request.URL.Path != "/" {
		http.NotFound(response, request)
		return
	}
	response.Header().Set("Content-Type", "text/html; charset=utf-8")
	response.Header().Set("Cache-Control", "no-store")
	_, _ = response.Write([]byte(wholeHomePanelHTML))
}

const wholeHomePanelHTML = `<!doctype html>
<html lang="pl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Cały dom · Junexis</title>
  <style>
    :root { color-scheme: dark; font-family: Inter, ui-sans-serif, system-ui, sans-serif; background: #07111f; color: #edf6ff; }
    * { box-sizing: border-box; }
    body { margin: 0; min-height: 100vh; background: radial-gradient(circle at 12% 0%, #173d63 0, #07111f 40%); }
    main { width: min(1120px, calc(100% - 32px)); margin: 0 auto; padding: 48px 0 72px; }
    header { display: flex; align-items: end; justify-content: space-between; gap: 24px; margin-bottom: 28px; }
    .eyebrow { color: #71d8ff; font-size: .78rem; font-weight: 800; letter-spacing: .16em; text-transform: uppercase; }
    h1 { margin: 8px 0 7px; font-size: clamp(2.2rem, 5vw, 4rem); line-height: .98; }
    .subtitle { margin: 0; color: #a8bdd2; }
    .live { display: inline-flex; align-items: center; gap: 9px; padding: 9px 14px; border: 1px solid #28506f; border-radius: 999px; color: #bfefff; background: #0d2235; white-space: nowrap; }
    .dot { width: 9px; height: 9px; border-radius: 50%; background: #38e59d; box-shadow: 0 0 16px #38e59d; }
    .summary { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; margin-bottom: 18px; }
    .summary-card { padding: 17px 20px; border: 1px solid #24506e; border-radius: 16px; background: rgba(13, 34, 53, .92); }
    .label { color: #8eabc2; font-size: .72rem; font-weight: 800; letter-spacing: .1em; text-transform: uppercase; }
    .summary-value { margin-top: 7px; font-size: 1.35rem; font-weight: 760; }
    .rooms { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    .room { min-height: 350px; padding: 22px; border: 1px solid #1d3b54; border-radius: 22px; background: rgba(10, 28, 44, .9); box-shadow: 0 20px 45px rgba(0,0,0,.2); }
    .room h2 { margin: 7px 0 3px; font-size: 1.75rem; }
    .room-status { color: #829bb0; font-size: .88rem; }
    .reading { display: flex; align-items: baseline; gap: 7px; margin: 25px 0 20px; }
    .ppm { font-size: 2.8rem; font-weight: 800; line-height: 1; }
    .unit { color: #8fcde7; font-weight: 650; }
    .meter { height: 8px; overflow: hidden; border-radius: 999px; background: #172f43; }
    .meter span { display: block; width: 0; height: 100%; border-radius: inherit; background: #4fdca0; transition: width .35s ease, background .35s ease; }
    dl { display: grid; grid-template-columns: auto 1fr; gap: 9px 12px; margin: 24px 0 0; }
    dt { color: #7895ac; } dd { margin: 0; text-align: right; font-weight: 680; overflow-wrap: anywhere; }
    .on { color: #51f0aa; } .off { color: #ffcf70; } .waiting { color: #91a9bc; }
    footer { margin-top: 22px; color: #7892a8; font-size: .88rem; line-height: 1.5; }
    @media (max-width: 900px) { .rooms { grid-template-columns: 1fr; } .room { min-height: auto; } }
    @media (max-width: 620px) { header { align-items: start; flex-direction: column; } .summary { grid-template-columns: 1fr; } }
  </style>
</head>
<body>
<main>
  <header>
    <div><div class="eyebrow">Laboratorium Junexis · Dom 5</div><h1>Cały dom</h1><p class="subtitle">Trzy źródła · jedna reguła · trzy potwierdzone stany</p></div>
    <div class="live"><span class="dot"></span><span id="connection">czekam na dane</span></div>
  </header>
  <section class="summary" aria-label="Podsumowanie domu">
    <article class="summary-card"><div class="label">Znane pomieszczenia</div><div id="known" class="summary-value">0 z 3</div></article>
    <article class="summary-card"><div class="label">Aktywna wentylacja</div><div id="active" class="summary-value">0 pomieszczeń</div></article>
    <article class="summary-card"><div class="label">Najwyższy pomiar</div><div id="highest" class="summary-value">—</div></article>
  </section>
  <section class="rooms" aria-label="Stan pomieszczeń">
    <article class="room" data-room="salon"><div class="label">Pomieszczenie</div><h2>Salon</h2><div class="room-status">brak pierwszego potwierdzenia</div><div class="reading"><span class="ppm">—</span><span class="unit">ppm</span></div><div class="meter"><span></span></div><dl><dt>Próg</dt><dd class="threshold">—</dd><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wentylacja</dt><dd class="ventilation waiting">oczekiwanie</dd><dt>Dlaczego?</dt><dd class="cause">—</dd></dl></article>
    <article class="room" data-room="kuchnia"><div class="label">Pomieszczenie</div><h2>Kuchnia</h2><div class="room-status">brak pierwszego potwierdzenia</div><div class="reading"><span class="ppm">—</span><span class="unit">ppm</span></div><div class="meter"><span></span></div><dl><dt>Próg</dt><dd class="threshold">—</dd><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wentylacja</dt><dd class="ventilation waiting">oczekiwanie</dd><dt>Dlaczego?</dt><dd class="cause">—</dd></dl></article>
    <article class="room" data-room="sypialnia"><div class="label">Pomieszczenie</div><h2>Sypialnia</h2><div class="room-status">brak pierwszego potwierdzenia</div><div class="reading"><span class="ppm">—</span><span class="unit">ppm</span></div><div class="meter"><span></span></div><dl><dt>Próg</dt><dd class="threshold">—</dd><dt>Decyzja</dt><dd class="decision">—</dd><dt>Wentylacja</dt><dd class="ventilation waiting">oczekiwanie</dd><dt>Dlaczego?</dt><dd class="cause">—</dd></dl></article>
  </section>
  <footer>Każda karta zachowuje ostatnie potwierdzenie własnego pomieszczenia. Podsumowanie jest projekcją tych trzech niezależnych stanów — nie zastępuje ich jedną wartością.</footer>
</main>
<script>
const byId = id => document.getElementById(id);
const cards = new Map([...document.querySelectorAll('[data-room]')].map(card => [card.dataset.room, card]));
function renderRoom(state) {
  const card = cards.get(state.room); if (!card) return;
  card.querySelector('.room-status').textContent = 'potwierdzono ' + new Intl.DateTimeFormat('pl-PL', {hour:'2-digit',minute:'2-digit',second:'2-digit'}).format(new Date(state.confirmed_at));
  card.querySelector('.ppm').textContent = state.air_quality.toFixed(0);
  card.querySelector('.threshold').textContent = state.threshold.toFixed(0) + ' ppm';
  card.querySelector('.decision').textContent = state.decision;
  card.querySelector('.cause').textContent = state.cause;
  const ventilation = card.querySelector('.ventilation');
  ventilation.textContent = state.actual_enabled ? 'WŁĄCZONA' : 'WYŁĄCZONA';
  ventilation.className = 'ventilation ' + (state.actual_enabled ? 'on' : 'off');
  const percent = Math.min(100, Math.max(4, state.air_quality / 20));
  const meter = card.querySelector('.meter span'); meter.style.width = percent + '%'; meter.style.background = state.air_quality > state.threshold ? '#ffb45c' : '#4fdca0';
}
async function refresh() {
  try {
    const response = await fetch('/api/home', {cache:'no-store'}); const home = await response.json();
    byId('connection').textContent = home.summary.known_rooms === 3 ? 'cały dom działa' : 'poznaję pomieszczenia';
    byId('known').textContent = home.summary.known_rooms + ' z 3';
    const activeRooms = home.summary.ventilating_rooms;
    byId('active').textContent = activeRooms + (activeRooms === 1 ? ' pomieszczenie' : (activeRooms >= 2 && activeRooms <= 4 ? ' pomieszczenia' : ' pomieszczeń'));
    byId('highest').textContent = home.summary.highest_room ? home.summary.highest_room + ' · ' + home.summary.highest_air_quality.toFixed(0) + ' ppm' : '—';
    home.rooms.forEach(renderRoom);
  } catch (_) { byId('connection').textContent = 'brak połączenia'; }
}
refresh(); setInterval(refresh, 500);
</script>
</body>
</html>`
