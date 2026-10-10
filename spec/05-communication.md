# Junexis Lang — Komunikacja

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

---

## Komunikacja jako fundament

W Junexis Lang komunikacja nie jest biblioteką — jest częścią systemu typów. Trzy typy komunikacyjne (`chan<T>`, `queue<T>`, `stream<T>`) mają odmienną semantykę dostawcy i różne gwarancje, celowo zaprojektowane dla konkretnych use-case'ów.

**Nadrzędna zasada:** Jeśli wybierzesz zły typ komunikacyjny, kompilator ostrzeże (diagnostyki `W/E 4201-4212`). Jeśli wybierzesz dobry — adapter protokołu jest generowany automatycznie.

---

## Trzy typy komunikacyjne

### `chan<T>` — synchroniczny kanał

| Cecha | Wartość |
|-------|---------|
| Semantyka | Point-to-point, synchronous |
| Gwarancja dostarczenia | **Zero loss** — producent blokuje jeśli nie ma konsumenta |
| Trwałość | Brak (in-memory) |
| Backpressure | **Tak** — producent się zatrzymuje przy przepełnieniu bufora |
| Model | 1→1 |
| Typowy protokół | TCP / gRPC / AMQP direct |

**Kiedy używać `chan<T>`:**
- Komunikacja RPC-like: żądanie–odpowiedź
- Control plane: komendy, konfiguracja
- Gdy producent musi wiedzieć, że wiadomość dotarła
- Krótkie, synchroniczne łańcuchy przetwarzania

**Kiedy NIE używać:**
- Fanout 1→N (użyj `stream<T>`)
- Offline-first / retry (użyj `queue<T>`)
- Embedded bez gwarancji połączenia

```junexis
task Controller() @server -> chan<Command>
task Motor(cmd: chan<Command>) @embedded(esp32c3) {
    let received: Command = recv cmd
    // producent zablokował się dopóki ta linia się nie wykonała
}
```

---

### `queue<T>` — kolejka trwała

| Cecha | Wartość |
|-------|---------|
| Semantyka | At-least-once delivery |
| Gwarancja dostarczenia | **At-least-once** — wiadomość nie jest tracona |
| Trwałość | Tak (WAL/disk lub in-memory z flush) |
| Backpressure | Nie — producent nie blokuje (buforuje) |
| Model | 1→1 (FIFO) |
| Wymaga | ACK/NACK od konsumenta |
| Typowy protokół | MQTT, AMQP, Kafka, własny WAL |

**Kiedy używać `queue<T>`:**
- Offline-first: urządzenie może być chwilowo offline
- Event sourcing: każde zdarzenie musi być przetworzone
- Audit log: gwarancja kompletności historii
- Job queue: zadania nie mogą być pomijane

**Kiedy NIE używać:**
- Gdy potrzebujesz najnowszej wartości, nie historii (użyj `stream<T>`)
- Real-time streaming wysokiego wolumenu (koszty trwałości)
- `@embedded` bez deklaracji `storage_budget` (kompilator emituje `E4210`)

```junexis
task EventCollector() @server -> queue<SensorEvent>

task EventProcessor(events: queue<SensorEvent>) @server {
    for event in events {
        process(event)
        // implicit ACK po poprawnym przetworzeniu
    }
}
```

**ACK/NACK semantics:**

Konsument automatycznie **ACK-uje** wiadomość po wyjściu z bloku przetwarzania bez błędu. Jawny NACK przy wyjątku lub ręczny przez `nack event`:

```junexis
for event in events {
    match validate(event) {
        Error(msg) -> {
            log("invalid event: {msg}")
            nack event          // wróci do kolejki z retry
        }
        _ -> process(event)     // implicit ACK
    }
}
```

**`@embedded` + `queue` (E4210):**

Embedded devices mają ograniczoną pamięć. Kompilator wymaga jawnego budżetu:

```junexis
task Sensor() @embedded(esp32c3) -> queue<TempReading> {
    config {
        storage_budget:  Memory : 64KB   // wymagane przez kompilator
        eviction_policy: Symbol : drop_oldest
    }
}
```

Bez `storage_budget` kompilator emituje `E4210: queue<T> on @embedded requires storage_budget in config`.

---

### `stream<T>` — strumień broadcast

| Cecha | Wartość |
|-------|---------|
| Semantyka | Latest-wins / broadcast |
| Gwarancja dostarczenia | **Może dropować** — stary odczyt zastępowany nowym |
| Trwałość | Brak (in-memory, najnowsza wartość) |
| Backpressure | Nie — producent nigdy nie blokuje |
| Model | 1→N (fanout) |
| Typowy protokół | WebSocket, MQTT retain, SSE |

**Kiedy używać `stream<T>`:**
- Telemetria, metryki, dashboard
- Real-time monitoring (chcesz *aktualną* wartość, nie każdą)
- Broadcast do wielu konsumentów
- Wideo, audio, sensor streams

**Kiedy NIE używać:**
- Gdy każde zdarzenie musi być przetworzone (użyj `queue<T>`)
- Control plane — ryzyko dropowania krytycznych komend

```junexis
task MetricCollector() @server -> stream<SystemMetrics>

// każdy konsument dostaje własną kopię przy subskrypcji
task Dashboard(metrics: stream<SystemMetrics>) @report {
    for reading in metrics {
        render_gauge(reading)   // może pominąć odczyty gdy jest za wolny
    }
}

task AlertEngine(metrics: stream<SystemMetrics>) @server {
    for reading in metrics {
        if reading.cpu_pct > 90.0 {
            send_alert("HIGH CPU", reading)
        }
    }
}
```

---

## Instrukcje komunikacyjne

### `send`

```
send Expression (to Expression)?  ;?
```

Wysyła wartość na kanał komunikacyjny.

```junexis
send SensorData { temp: 21.5, humidity: 60 }

// jawne wskazanie celu
send alert to alert_channel

// inline expression
send compute_payload()
```

Zachowanie przy `send` zależy od typu komunikacyjnego:
- `chan<T>`: blokuje producenta jeśli bufor pełny
- `queue<T>`: nie blokuje — zapisuje do bufora/WAL
- `stream<T>`: nie blokuje — zastępuje poprzednią wartość

### `recv`

```
(let|var) identifier = recv (from?) expression  ;?
recv (from?) expression  ;?
```

Odbiera wartość z kanału:

```junexis
let cmd: Command = recv input_chan

// pełna forma
let reading = recv from sensor_stream

// bez przypisania — side effect (rzadkie, np. draining queue)
recv from ack_chan
```

Zachowanie:
- `chan<T>`: blokuje do momentu pojawienia się wartości
- `queue<T>`: blokuje do momentu pojawienia się wiadomości (lub timeout)
- `stream<T>`: zwraca od razu (najnowsza wartość) lub blokuje przy subskrypcji

### `respond`

Specjalna forma `send` dla wzorców request-response na `chan<T>`. Semantycznie equivalentna `send`, ale sygnalizuje intencję odpowiedzi.

```junexis
task Calculator(requests: chan<CalcRequest>) @server {
    for req in requests {
        let result = compute(req)
        respond result
    }
}
```

---

## Wzorce komunikacyjne

### Fanout (1→N)

`stream<T>` wspiera fanout natywnie:

```junexis
task DataSource() @server -> stream<Measurement>

pipeline Monitoring {
    use DataSource -> Dashboard
    use DataSource -> AlertEngine    // ta sama instancja DataSource
    use DataSource -> DataArchiver
}
```

Dla `chan<T>` fanout wymaga jawnego broadcast bridge (kompilator emituje `E4203: chan fanout requires explicit bridge`).

### Pipeline chain (N→1 aggregation)

```junexis
task Aggregator(
    temp: chan<TempData>,
    humidity: chan<HumidityData>,
    pressure: chan<PressureData>
) @server -> chan<EnvReading> {
    for {
        let t = recv temp
        let h = recv humidity
        let p = recv pressure
        send EnvReading { temp: t.value, humidity: h.value, pressure: p.value }
    }
}
```

### Dead Letter Queue (DLQ)

Wiadomości z `queue<T>` które przekroczyły `max_retries` trafiają do DLQ. Kompilator generuje infrastrukturę automatycznie:

```junexis
task EventSink(events: queue<Event>) @server {
    config {
        max_retries: Int : 3
        dlq_name: Text : "failed_events"
    }

    for event in events {
        process(event)
    }
}
```

---

## Diagnostyki komunikacyjne

| Kod | Poziom | Kiedy |
|-----|--------|-------|
| `W4201` | Warning | `stream<T>` połączony z task wymagającym zero-loss |
| `W4202` | Warning | `queue<T>` bez deklaracji ACK/NACK handling |
| `E4203` | Error | `chan<T>` fanout bez broadcast bridge |
| `I4204` | Info | `queue<T>` połączony przez restart — dobra praktyka |
| `E4210` | Error | `queue<T>` na `@embedded` bez `storage_budget` |
| `W4211` | Warning | `queue<T>` bez konsumenta (eskalacja do E bez offload) |
| `W4212` | Warning | Wiele kolejek bez limitów na `@embedded` |

---

## Tabela decyzyjna

| Pytanie | `chan<T>` | `queue<T>` | `stream<T>` |
|---------|-----------|------------|-------------|
| Każde zdarzenie musi dotrzeć? | ✅ | ✅ | ❌ |
| Producent może blokować? | ✅ | ❌ | ❌ |
| Wielu niezależnych konsumentów? | ❌ | ❌ | ✅ |
| Potrzebuję retry/DLQ? | ❌ | ✅ | ❌ |
| Device może być offline? | ❌ | ✅ | ❌ |
| Chcę najnowszą wartość, nie historię? | ❌ | ❌ | ✅ |
| @embedded z ograniczoną pamięcią? | ✅ | ⚠️ (+ budget) | ✅ |
