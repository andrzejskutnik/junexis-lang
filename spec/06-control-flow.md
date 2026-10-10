# Junexis Lang — Przepływ sterowania i wyrażenia

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

---

## Przegląd

Junexis Lang posiada standardowy zestaw konstrukcji przepływu sterowania, uzupełniony o elementy specyficzne dla systemów rozproszonych: `every` (pętla czasowa), `delay` (wstrzymanie), `match` z pattern matching. Wszystkie konstrukcje są wyrażeniami lub instrukcjami — specyfikacja rozróżnia te dwa przypadki tam, gdzie ma to znaczenie.

---

## Deklaracje zmiennych

```
(let | const | var) name (: Type)? = expression  ;?
```

| Słowo kluczowe | Mutowalnośc | Opis |
|----------------|-------------|------|
| `let` | niemutwalne po przypisaniu | Preferowane — wartość nie zmienia się |
| `const` | niemutatywne | Stała — musi być wyrażeniem stałym (literal lub `fn`) |
| `var` | mutowalne | Użyj gdy wartość się zmienia |

```junexis
let sensor_id = "temp-01"
let threshold: Float = 40.0
const MAX_RETRIES: Int = 3
var counter: Int = 0
```

Adnotacja typu jest opcjonalna (inferencja w 0.2 best-effort). Dla `var` typ może być deklarowany explicite gdy kompilator nie może wywnioskować.

---

## `state` — persistentny stan tasku

```
state name : Type (= expression)?  ;?
```

`state` deklaruje zmienną persistentną w kontekście `task`. Różni się od `let`/`var`:

- Przechowywana przez kompilat (nie stosowy `var`) — dla `@embedded` oznacza zmienną globalną lub w NVRAM.
- Widoczna w całym body tasku, w tym w hooks.
- Przypisanie: `state name = newValue` (bez `let`/`var` przy aktualizacji).

```junexis
task TempMonitor() @embedded {
    state last_temp: Float = 0.0
    state alert_count: Int = 0

    every 5s {
        let t = read_temp()
        if t > 40.0 {
            state alert_count = alert_count + 1
        }
        state last_temp = t
    }
}
```

---

## `if / else`

```
if expression { statements }
(else if expression { statements })*
(else { statements })?
```

Standardowy warunkowy. Brak nawiasów przy warunku (zamiast C-style). Wyrażenie warunkowe musi być `Bool`.

```junexis
if temp > threshold {
    send_alert("HIGH_TEMP", temp)
} else if temp < 0.0 {
    send_alert("FREEZE", temp)
} else {
    log("temp normal: {temp}")
}
```

---

## `for`

Jedyne słowo kluczowe pętli w Junexis — trzy warianty składniowe:

```
for { statements }                           // nieskończona
for expression { statements }               // warunkowa (dopóki true)
for identifier in expression { statements } // iteracja po kolekcji/zakresie/kanale
```

### Nieskończona

Używana dla tasks które permanentnie nasłuchują lub przetwarzają. Wyjście przez `break` lub zakończenie tasku z zewnątrz.

```junexis
task Watchdog() @server {
    for {
        let ping = recv heartbeat
        if ping.delay > 5s {
            log("WARNING: heartbeat delay {ping.delay}")
        }
    }
}
```

### Warunkowa

Wykonuje ciało dopóki warunek jest `true`.

```junexis
var retries = 0
for retries < 3 {
    let ok = try_connect()
    if ok { break }
    retries = retries + 1
    delay 1s
}
```

### Iteracja po kolekcji, zakresie lub kanale

```junexis
// iteracja po liście
for sensor in active_sensors {
    log("sensor: {sensor.id}")
}

// iteracja po queue — blokuje przy braku wiadomości
for event in event_queue {
    process(event)
}

// zakres (range expression)
for i in 0..10 {
    log("step {i}")
}
```

Iteracja po `queue<T>` jest trybem ciągłym — pętla wykonuje się dopóki kolejka nie jest zamknięta lub task nie jest zatrzymany.

---

## `every`

```
every expression { statements }
```

Pętla czasowa — wykonuje ciało co określony interwał (`Duration`). Specyficzna dla Junexis. Kompilator generuje odpowiedni timer/ticker dla każdej platformy:

- `@embedded` → `vTaskDelay` (FreeRTOS) lub `delay()` (Arduino)
- `@server` → `time.NewTicker` (Go) / `PeriodicTimer` (C#)
- `@report` → `setInterval` (TypeScript)

```junexis
task TempEmitter() @embedded(esp32c3) -> chan<TempData> {
    every 2s {
        let raw = adc_read(4)
        send TempData { celsius: raw * 0.1, timestamp: now() }
    }
}
```

Wyrażenie czasu musi być typu `Duration` (literał lub zmienna):

```junexis
config { poll_interval: Duration : 500ms }

every config.poll_interval {
    // ...
}
```

---

## `delay`

```
delay expression  ;?
```

Wstrzymuje wykonanie na `Duration`. Asynchroniczne — nie blokuje całego systemu, tylko bieżący task.

```junexis
delay 100ms
delay config.retry_wait
```

---

## `match`

```
match expression {
    pattern -> (expression | { statements })  ,?
    ...
}
```

Pattern matching — podstawowy mechanizm pracy z typami unii i wartościami enum. Wzorce są sprawdzane po kolei; pierwszy pasujący wzorzec wygrywa.

### Wzorce

| Wzorzec | Opis | Przykład |
|---------|------|---------|
| `_` | Wildcard — pasuje do wszystkiego | `_ -> log("other")` |
| Literal | Pasuje do konkretnej wartości | `42 -> ...` |
| Identifier | Binds wartość | `x -> log("got {x}")` |
| Enum variant | `Enum::Variant` | `Color::Red -> ...` |
| Enum variant z danymi | `Enum::Variant(binding)` | `Result::Ok(v) -> use(v)` |
| Enum tuple variant | `Enum::Variant(a, b)` | `Shape::Rect(w, h) -> ...` |

```junexis
match status {
    Status::Active          -> restart_heartbeat()
    Status::Error(msg)      -> log("error: {msg}")
    Status::Inactive        -> { delay 10s; reconnect() }
    _                       -> log("unknown status")
}

// match wyrażenie (zwraca wartość)
let label = match priority {
    0 -> "critical"
    1 -> "high"
    2 -> "normal"
    _ -> "low"
}
```

---

## `return`

```
return expression?  ;?
```

Zwraca wartość z `fn`. Może być użyte do wczesnego wyjścia z `task` (inicjuje graceful shutdown). Opcjonalne `expression` gdy zwracamy pustą/unit wartość.

```junexis
fn safe_divide(a: Float, b: Float) -> Float | Error {
    if b == 0.0 {
        return Error("division by zero")
    }
    return a / b
}
```

---

## `break` i `continue`

Wewnątrz pętli (`for`):

- `break` — wychodzi z bieżącej pętli
- `continue` — przechodzi do następnej iteracji

```junexis
for item in batch {
    if item.invalid {
        log("skipping invalid item")
        continue
    }
    if processed_count > limit {
        break
    }
    process(item)
}
```

> **Uwaga:** `break` i `continue` NIE są zdefiniowane jako tokeny w 0.2 gramatyce. Są parsowane jako identyfikatory (IDENTIFIER). Kompilator w fazie semantycznej rozpoznaje je jako słowa kluczowe. Planowane jako tokeny w 0.3.

---

## `log`

```
log(expression (, expression)*)  ;?
```

Wbudowana funkcja logowania. Nie jest pełnoprawną instrukcją — jest call expression z priorytetem. String interpolation w literałach przez `{expression}`:

```junexis
log("temp={temp}, alert_count={alert_count}")
log("status:", status, "at", timestamp)
```

---

## Wyrażenia

### Priorytety operatorów (od najniższego)

| Priorytet | Operator(y) | Opis |
|-----------|-------------|------|
| 1 (najniższy) | `= += -= *= /= %=` | Przypisanie |
| 2 | `? :` | Conditional expression |
| 3 | `\|\|` / `or` | Logiczne OR |
| 4 | `&&` / `and` | Logiczne AND |
| 5 | `== !=` | Równość |
| 6 | `< > <= >=` | Relacyjne |
| 7 | `+ -` | Addytywne |
| 8 | `* / %` | Multiplikatywne |
| 9 | `as` | Cast / konwersja typu |
| 10 | `-` `not` / `!` `&` | Unarne |
| 11 | `[] . :: ()` | Postfix |
| 12 (najwyższy) | Literal, identifier, `()` | Primary |

### Operatory

**Arytmetyczne:** `+`, `-`, `*`, `/`, `%`

```junexis
let area = width * height
let avg = (a + b + c) / 3.0
let remainder = total % page_size
```

**Porównania:** `==`, `!=`, `<`, `>`, `<=`, `>=`

```junexis
if temp >= threshold { send_alert() }
let different = a != b
```

**Logiczne:** `and` / `&&`, `or` / `||`, `not` / `!` (unarne)

Junexis preferuje słowa kluczowe (`and`, `or`, `not`), lecz akceptuje także
`&&`, `||`, `!`. Formy są semantycznie równoważne, a formatter normalizuje je
do postaci słownej.

```junexis
if connected and battery > 20 { transmit() }
if not initialized { setup() }
```

**Dostęp do pola:** `.`

```junexis
let host = config.network.host
let pin = device.hardware.dht22_pin
```

**Dostęp do wariantu enum:** `::`

```junexis
let s = Status::Error("timeout")
match s {
    Status::Error(msg) -> log(msg)
    _                  -> log("ok")
}
```

**Indeksowanie:** `[]`

```junexis
let first = sensor_list[0]
let val = lookup_table["key"]
```

**Cast:** `as`

```junexis
let raw: u16 = read_adc()
let temp: Float = raw as Float * 0.1
```

**Ternary:** `condition ? value_true : value_false`

```junexis
let label = is_active ? "on" : "off"
```

**Range:** `..`

```junexis
for i in 0..100 {
    // 0, 1, ..., 99 (koniec exclusive)
}
```

### String interpolation

Wewnątrz literałów `""` wyrażenia w `{}` są interpolowane:

```junexis
let msg = "temp={temp_value:.2f}, status={status}"
log("sensor {device.id} ready at {device.network.host}:{port}")
```

Format specifiers (`:fmt`) są best-effort w 0.2 — kompilator generuje `sprintf`/`fmt.Sprintf`/template literals dla platform docelowych.

### Struct literal

```junexis
let reading = SensorReading {
    temperature: 21.3
    humidity: 65.0
    timestamp: now()
}
```

### Array literal

```junexis
let ids: [Text] = ["sensor-01", "sensor-02", "sensor-03"]
let empty: [Int] = []
```

### JSON-like object literal

Wewnątrz `{...}` z kluczami w cudzysłowie — interpretowane jako `Map<Text, T>`:

```junexis
let meta: Map<Text, Text> = { "source": "sensor", "region": "eu-west" }
```

---

## Komunikacja w wyrażeniach

`recv` i `send` mogą być użyte jako wyrażenia (inline):

```junexis
// recv jako wyrażenie
let value = recv from data_channel

// send jako wyrażenie (zwraca unit)
let _ = send result to output
```

---

## Brak wyjątków (0.2)

Junexis Lang 0.2 **nie posiada `try/catch/throw`**. Obsługa błędów przez:

1. **Union types**: `fn parse(s: Text) -> Int | Error`
2. **Pattern matching** na wynikach
3. **`supervisor`** (EXPERIMENTAL) dla restart strategies
4. **`circuit_breaker`** (EXPERIMENTAL) dla software-level resilience

```junexis
let result = parse_int(input)
match result {
    n: Int  -> process(n)
    e: Error -> log("parse error: {e}")
}
```
