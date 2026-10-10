# Junexis Lang — Deklaracje top-level

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

---

## Przegląd

Junexis posiada zestaw projektowanych deklaracji na poziomie programu (top-level).
Poniższe tabele rozróżniają je według strategii parsowania i dojrzałości semantyki.

Projekt D17 proponuje ogólną deklarację `resource`, która nadal nie jest
częścią gramatyki. M13.2 wprowadza węższą, relacyjną deklarację `storage`.
M13.3–M13.9 prowadzą ją przez neutralny Query Plan, Runtime i trzy providery
do generowanego klienta Go. Od M13.10 named query można wywołać bezpośrednio
z tasku `@server`, M13.11 pozwala importować jego model z osobnego modułu, a
M13.12 dodaje lokalny alias bez zmiany tożsamości Runtime. M13.13 udostępnia
pochodne funkcje i typy również w completion, hover, Go to Definition i
Outline workspace IDE. M14.1 ustanawia neutralny Mutation Plan i jego reguły
bezpieczeństwa, M14.2 dodaje składnię mutacji i projekcję źródłową, a M14.3
neutralny lowering `mutation.plan` przez Storage Runtime ABI.

### CORE — ustrukturyzowane

Parser analizuje ich strukturę zamiast traktować ciało jako opaque. Pokrycie AST,
semantyki, IR i codegenu jest nierówne, a format może zmieniać się w ramach 0.2.

| Deklaracja | Parser | Opis |
|------------|--------|------|
| `task` | structured | Zadanie/usaługa. Podstawowy building block |
| `struct` | structured | Struktura danych (product type) |
| `enum` | structured | Enumeracja (sum type) |
| `fn` | structured | Czysta funkcja (bez side effects wejścia/wyjścia) |
| `pipeline` | structured | Potok danych łączący tasks |
| `device` | structured | Urządzenie sprzętowe |
| `import` | structured | Import modułu |
| `storage` | structured, EXPERIMENTAL | Logiczne encje, wykonywalne query read-only M13 oraz walidowane mutacje M14.2 |

### EXPERIMENTAL: `storage` (M13.2)

```junexis
storage HomeData {
    entity Observation {
        id: Uuid
        value: Float
        recordedAt: Time
        key(id)
    }

    query Recent(limit: Int) from Observation {
        project value, recordedAt
        sort recordedAt desc
        limit 20
    }
}
```

Parser analizuje całą strukturę. Projektor sprawdza przenośne typy, klucz,
parametry i kształt po każdym kroku, po czym tworzy
`junexis.storage.model/0.1` oraz `junexis.storage.query-plan/0.1`. Szczegółowy
kontrakt i jego granice opisuje
[`43-storage-source-projection.md`](43-storage-source-projection.md).

Od M13.10 query tworzy kwalifikowaną funkcję oraz syntetyczny typ wiersza:

```junexis
task Read(limit: Int) @server -> List<HomeData_RecentRow> {
    return HomeData_Recent(limit: limit)
}
```

Wywołanie jest obecnie wykonywalne tylko w tasku `@server`. Szczegóły
typowania, lifecycle i diagnostyki opisuje
[`51-storage-task-query-binding.md`](51-storage-task-query-binding.md).

W projekcie model można utrzymywać w osobnym module i importować bez
powtarzania funkcji query:

```junexis
import data::home { HomeData }

task Read(limit: Int) @server -> List<HomeData_RecentRow> {
    return HomeData_Recent(limit: limit)
}
```

Import może zachować kanoniczną nazwę modelu albo nadać mu alias lokalny:

```junexis
import data::home { HomeData as Store }

task Read(limit: Int) @server -> List<Store_RecentRow> {
    return Store_Recent(limit: limit)
}
```

`Store` nie trafia do Query Plan ani Runtime. Reguły projektu wieloplikowego
opisują [`52-imported-storage-query-binding.md`](52-imported-storage-query-binding.md)
i [`53-storage-import-alias.md`](53-storage-import-alias.md). Zachowanie tych
symboli w IDE opisuje
[`54-storage-query-developer-experience.md`](54-storage-query-developer-experience.md).

Od M14.2 ten sam model może deklarować bezpieczną intencję zapisu:

```junexis
mutation RenameSensor(id: Uuid, sensor: Text)
    update single Observation {
    set sensor = params.sensor
    where id == params.id
}
```

`single` wymaga pełnego klucza, a zapis masowy wymaga jawnego `many`.
Kompilator projektuje deklarację do Mutation Plan i neutralnego deskryptora
Runtime `mutation.plan`. Od M14.4 Host i provider memory wykonują ten deskryptor;
od M14.5 tę samą semantykę wykonuje trwale SQLite. Od M14.10 task `@server`
może wywołać nazwaną mutation przez symbol `<Model>_<Mutation>(...)` i otrzymać
jawny wynik `<Model>_<Mutation>Result` z `outcome` oraz `affectedRows`.
Składnię opisuje
[`56-storage-mutation-source-projection.md`](56-storage-mutation-source-projection.md),
a lowering i wynik zapisu
[`57-storage-runtime-mutation-lowering.md`](57-storage-runtime-mutation-lowering.md).
Binding z taskiem opisuje
[`64-storage-task-mutation-binding.md`](64-storage-task-mutation-binding.md).

### EXPERIMENTAL — opaque (Decyzja D3)

Parser zbiera jedynie balanced braces — `{ ... }` — bez analizowania treści.
Kompilator przekazuje zawartość jako string do wyspecjalizowanych procesorów.
Od M4d wykonywalny podzbiór `protocol rest` oraz `deployment target` jest
walidowany i loweringowany przez Distributed Topology IR. Pozostała treść nadal
ma semantykę eksperymentalną.

> **Decyzja D3 — rationale:** Opaque parsing umożliwia ewolucję metadanych bez
> zmian gramatyki. Weryfikacja należy do procesorów semantycznych, nie parsera.

| Deklaracja | Parser | Opis |
|------------|--------|------|
| `protocol` | opaque + semantic lowering | Binding task ↔ protokół; REST ma wykonywalny baseline 0.2 |
| `narrative` | opaque | Metadane narracyjne (obserwability, intent, compliance) |
| `deployment` | opaque + semantic lowering | `target` placement i replicas mają wykonywalny baseline 0.2 |
| `supervisor` | opaque | Drzewo superwizji (fault tolerance) |
| `circuit_breaker` | opaque | Wzorzec circuit breaker — automatyczne otwieranie obwodu po przekroczeniu progu błędów, ochrona przed kaskadowymi awariami |
| `quirks` | opaque | Obejścia sprzętowe (embedded workarounds) |
| `extern task` | structured | FFI bridge — deklaracja tasku z implementacją w języku natywnym (D13) |
| `extern type` | structured | Opaque handle na typ natywny — nieprzezroczysty dla JunexisLang (D13) |

---

## CORE: task

**Składnia:**

```
task Name (parameterList?) platformAnnotation* -> ReturnType? { body }
```

**Komponenty zadania:**

```junexis
task TempSensor(
    pin:     u8 = 4,       // parametr z wartością domyślną
    period:  Duration = 1s
) @embedded(esp32c3)
  -> chan<SensorReading>   // typ zwracany — kanał wyjściowy
{
    state last_reading: Float = 0.0  // stan persistentny tasku

    config {                         // parametry konfigurowalne z zewnątrz
        buffer_size:      Int   : 10
        alert_threshold:  Float : 40.0
    }

    on start {                       // hook — wykonywany raz przy uruchomieniu
        log("sensor started on pin {pin}")
    }

    every period {                   // pętla czasowa — co `period`
        let raw  = read_adc(pin)
        let temp = raw * 0.1
        state last_reading = temp    // aktualizacja stanu
        send SensorReading { temp: temp, ts: now() }  // emisja na kanał
    }
}
```

**Elementy body tasku:**

| Element | Status | Opis |
|---------|--------|------|
| `state name: T = v` | CORE | Persistentny stan tasku |
| `config { ... }` | CORE | Blok konfiguracyjny |
| `native { }` | CORE | Zależności natywne; głównie dla `@embedded` (D13). Składniowo identyczne jak w `device`. |
| `on <event> { }` | CORE | Hook cyklu życia — patrz tabela poniżej (D15) |
| Statements | CORE | Kod logiki (if, for, match, send, ...) |

**Lifecycle hooks — składnia i semantyka (D15):**

```
on <event_identifier> { statement* }
```

Dwa tokeny: keyword `on` + dowolny identyfikator. Kompilator (IR stage) waliduje, czy identyfikator jest zdarzeniem spełnionym w danym kontekście tasku i platformy. Nieznanego identyfikatora — błąd `E0013`.

**Zdarzenia wbudowane 0.2:**

| Identyfikator | Kiedy uruchamiany | Dostępny dla |
|---------------|-------------------|---------------|
| `start` | Raz, po inicjalizacji tasku — przed pierwszym `every` lub odebraniem wiadomości | Wszystkie tasks |
| `connect` | Gdy nawiązuje się połączenie przychodzące (peer zaczyna wysyłać) | Tasks z wejściowym `chan<T>` lub endpointem protokołu |
| `reconnect` | Gdy zerwane połączenie zostaje przywrócone | j.w. |
| `disconnect` | Gdy połączenie zostaje zerwane (peer się rozłączył lub timeout) | j.w. |

Hooki są **generowanymi punktami wejścia** — kompilator wstrzykuje wywołanie we właściwe miejsce wygenerowanego kodu. Developer nie może ich podnosić ręcznie ani definiować własnych w 0.2.

```junexis
task Motor(cmd: chan<Command>) @embedded(esp32c3) {
    on start {
        init_pwm(pin: 5)
        log("motor ready")
    }
    on connect {
        log("controller connected, awaiting commands")
    }
    on disconnect {
        stop_motor()
        log("controller lost, motor stopped")
    }
    on reconnect {
        log("controller back")
    }

    for {
        let c = recv cmd
        drive(c)
    }
}
```

Task `@server` bez wejściowego kanału — `on connect` wywoła błąd `E0013: event 'connect' not applicable`.

**Parametry tasku:**

Parametry mogą mieć wartości domyślne. Są przekazywane przy tworzeniu instancji (pipeline) lub deployment.

```junexis
task Logger(prefix: Text = "INFO", level: Int = 3) @server {
    recv msg: Text {
        log("{prefix}[{level}] {msg}")
    }
}
```

**Task a protokoły (D7):**

Task **nie deklaruje protokołu** — jest niezależny od warstwy komunikacji. Binding task ↔ protokół następuje przez osobną deklarację top-level `protocol { }`:

```junexis
// Taski — czysta logika domenowa, bez wiedzy o protokole
task ListUsers(limit: Int, offset: Int) @server -> List<User> { ... }
task CreateUser(dto: CreateUserDto) @server -> User { ... }
task Subscribe(req: StreamRequest) @server -> stream<DataPoint> { ... }

// Binding protokołów — osobno
protocol rest {
    GET("/users",  ListUsers(query limit: Int, query offset: Int))
    POST("/users", CreateUser(body dto: CreateUserDto))
}

protocol grpc {
    Subscribe
}
```

Jeden task może być zbindowany do wielu protokołów jednocześnie bez zmian w jego kodzie.

Szczegóły w [`08-protocols.md`](./08-protocols.md).

---

## CORE: struct

**Składnia:**

```
struct Name { field: Type (= default)? (; / ,)? ... }
```

Struktura może mieć opacjonalne wartości domyślne dla pól. Separatorem pól jest `;`, `,` lub ich brak — parser akceptuje wszystkie warianty.

```junexis
struct Config {
    host:     Text    = "localhost"
    port:     u16     = 8080
    timeout:  Duration = 30s
    debug:    Bool    = false
}

struct PaymentRequest {
    amount:   u64
    currency: Text
    card_token: Text
    metadata: Map<Text, Text>
}
```

Struktury są **niemutowalne po stworzeniu** — brak setterów, tylko destructuring i nowe instancje.

---

## CORE: enum

**Składnia:**

```
enum Name { Variant, Variant(T), Variant(T, U), ... }
```

Warianty mogą nosić dane (tuple warianty). Separatorem jest `,`.

```junexis
enum Color { Red, Green, Blue }

enum Result<T> {
    Ok(T),
    Err(Text)
}

enum Command {
    Start,
    Stop,
    SetValue(Float),
    Reset(Text, Int)
}
```

Dostęp do wariantu przez `::`:

```junexis
let color = Color::Red
let cmd = Command::SetValue(42.0)

match cmd {
    Command::Start        -> log("starting")
    Command::Stop         -> log("stopping")
    Command::SetValue(v)  -> log("set to {v}")
    Command::Reset(msg, n) -> log("reset: {msg} x{n}")
}
```

---

## CORE: fn

**Składnia:**

```
fn name(params?) (-> ReturnType)? { statements }
```

`fn` oznacza **czystą funkcję**: bez komunikacji (nie na kanałach), bez stanu globalnego, bez side effects I/O. Może wywołać inne `fn`. Kompilator wymusza brak `send`, `recv`, `state` wewnątrz `fn`.

**Rekurencja:**
- `@server`, `@report`: rekurencja dozwolona.
- `@embedded`: kompilator emituje `E0012: recursive fn not allowed on embedded` gdy `fn` jest rekurencyjna lub wywołuje rekurencyjny łańcuch `fn`. Ograniczenie wynika ze stałej alokacji stosu (D9).
- Opt-in: `fn` oznaczona `@allow_recursion` może być rekurencyjna na `@embedded`; developer bierze odpowiedzialność za głębokość stosu.

```junexis
fn factorial(n: u32) -> u64 {               // @embedded — E0012
    if n <= 1 { return 1 }
    return n * factorial(n - 1)
}

fn factorial(n: u32) @allow_recursion -> u64 {   // @embedded — OK
    if n <= 1 { return 1 }
    return n * factorial(n - 1)
}
```

```junexis
fn celsius_to_fahrenheit(c: Float) -> Float {
    return c * 9.0 / 5.0 + 32.0
}

fn clamp(value: Float, min: Float, max: Float) -> Float {
    if value < min { return min }
    if value > max { return max }
    return value
}

fn parse_duration(text: Text) -> Duration | Error {
    // ...
}
```

Różnica od `task`:

| Cecha | `fn` | `task` |
|-------|------|--------|
| Komunikacja (`send`/`recv`) | ❌ Niedozwolone | ✅ Ok |
| Stan persistentny (`state`) | ❌ Niedozwolone | ✅ Ok |
| Platform annotation | ❌ Brak | ✅ Wymagana |
| Cykl życia (hooks) | ❌ Brak | ✅ Ok |
| Rekurencja | ✅ `@server`/`@report`; `@embedded` wymaga `@allow_recursion` | — |
| Wywoływalność | Synchronicznie w kodzie | Pipeline/deployment |

---

## CORE: pipeline

**Składnia:**

```
pipeline Name { use Source (-> Target)? ; ... }
```

Pipeline definiuje graf przepływu danych między tasks. Opcja `parallel` dla równoległych rozgałęzień.

```junexis
pipeline SensorPipeline {
    use TempSensor -> DataProcessor
    use DataProcessor -> AlertEngine
    use DataProcessor -> DataStore
}

pipeline MultiSensor {
    parallel {
        use TempSensor -> Aggregator
        use HumiditySensor -> Aggregator
        use PressureSensor -> Aggregator
    }
    use Aggregator -> Dashboard
}
```

Dostęp do konkretnego wyjścia przez `Task.portName`:

```junexis
pipeline Routing {
    use Classifier.high_priority -> UrgentHandler
    use Classifier.normal -> NormalHandler
}
```

---

## CORE: device

**Składnia:**

```
device Name @platform(options?) { hardware { ... } native { ... } config { ... } }
```

Urządzenie opisuje fizyczny sprzęt — parametry hardware i referencje do bibliotek.

```junexis
device sensor_node @embedded(esp32c3) {
    hardware {
        dht22_pin:  u8 : 4
        led_pin:    u8 : 2
        i2c_sda:    u8 : 21
        i2c_scl:    u8 : 22
    }

    network {
        wifi_ssid:  Text : "HomeNet"
        wifi_pass:  Text : ""
        server_url: Text : "http://192.168.1.10"
    }

    timing {
        read_interval: Duration : 5s
        connect_timeout: Duration : 30s
    }

    native {
        @c/c++ {
            import: "dht22_adafruit"
        }
    }
}
```

Dostęp do pól urządzenia z kodu tasku: `device.hardware.dht22_pin`, `device.network.wifi_ssid`.

---

## `native`

`native` to sekcja wewnątrz `device` lub `task`. Opisuje zależności i inicjalizacje natywne dla konkretnych platform docelowych:

```junexis
native {
    @c/c++ {
        include: "<DHT.h>"
        library: "adafruit/DHT-sensor-library"
        use: "DHT dht;"
        init: "dht.begin({hardware.dht22_pin}, DHT22);"
    }
    @rust {
        import: "dht22_crate"
    }
}
```

Kompilator przy generacji kodu dla `@embedded` wstawia `#include`, `platformio.ini library_deps`, inicjalizacje.

---

## `config`

`config` to sekcja wewnątrz `task` lub `device`. Definiuje tuneable parameters:

```junexis
config {
    max_retries:     Int  : 3
    timeout:         Duration : 30s
    buffer_size:     Int  : 100
    enable_logging:  Bool : true
}
```

Format pola: `name: Type : default_value`

- `Type` — dowolny typ z systemu typów
- `default_value` — literał odpowiedniego typu (opcjonalne)
- Wartości mogą być nadpisane przez YAML deployment config

---

## EXPERIMENTAL: extern task (D13)

`extern task` deklaruje task którego **implementacja jest w pliku natywnym** (Go, C#, TypeScript, C++). Kompilator generuje bridge — sygnatura pochodzi z .jnx, ciało jest dostarczane przez developerów w języku docelowym.

```junexis
// .jnx — deklaracje
extern task SqlStore(data: SensorData) @server -> Bool
extern task SendEmail(to: Text, subject: Text) @server -> Bool
extern task DetectFace(frame: [u8]) @embedded(esp32s3) -> Float
```

Odpowiadające pliki natywne (nazewnictwo konwencjonalne, wymagane przez kompilator):

```go
// SqlStore.go — implementacja dla @server/Go
func JunexisSqlStore(data SensorData) bool {
    // ... SQL ...
    return true
}
```

```csharp
// SqlStore.cs — implementacja dla @server/C#
public static bool JunexisSqlStore(SensorData data) {
    // ... EF Core ...
    return true;
}
```

**Zasady:**
- `extern task` jest deklaracją — bez ciała `{ }`. Kończy się opcjonalnym `;
- Brak pliku natywnego → błąd `E0010: extern implementation not found`
- Niezgodna sygnatura (typy) → błąd `E0011: extern signature mismatch`
- Można bindingową `extern task` do `pipeline` jak każdą innyą deklarację tasku

**`extern task` a `junexis.toml`:**  
Jeśli `extern task` korzysta z bibliotek zewnętrznych, ich pakiety deklaruje się w `junexis.toml`:

```toml
# junexis.toml
[deps.go]
"github.com/lib/pq" = "^1.10"

[deps.csharp]
"Npgsql" = "8.0"
```

**Status:** EXPERIMENTAL w 0.2. Interfejs bridge (konwencja nazewnictwa `Junexis` prefix, mapowanie typów) może się zmienić w 0.3.

---

## EXPERIMENTAL: extern type (D13)

`extern type` deklaruje **opaque handle** na typ istniejący w kodzie natywnym. JunexisLang nie zna struktury wewnętrznej — widzi tylko nazwę i platformę docelową.

```junexis
extern type DbContext  @csharp("MyApp.Data.AppDbContext")
extern type SqlConn    @go("database/sql.DB")
extern type RenderNode @cpp("ui::RenderNode*")
```

`extern type` może być parametrem lub typem zwracanym wyłącznie w `extern task` na **tej samej platformie**:

```junexis
extern type DbContext @csharp("MyApp.Data.AppDbContext")

extern task OpenDb(connStr: Text)                    @server -> DbContext
extern task QueryUsers(db: DbContext, limit: Int)    @server -> List<User>
extern task CloseDb(db: DbContext)                   @server
```

JunexisLang nie serializuje, nie inspekcjonuje ani nie kopiuje wartości `extern type` — jedynie przekazuje referencję między wywołaniami `extern task`.

---

### Dwa modele wymiany danych na granicy natywnej

#### Model 1: JunexisLang `struct` jako schemat (transfer danych)

Gdy dane **przekraczają granicę** (kanał między platformami lub parametr `extern task`), schema pochodzi z `struct`. Kompilator generuje odpowiadający typ natywny oraz serializację:

```junexis
struct User { id: u64; name: Text; email: Text }

extern task SaveUser(user: User) @server -> Bool
//                   ↑ JunexisLang struct — kompilator generuje C# record + serializację
```

Kompilator generuje:
```csharp
// GENERATED — nie edytować
public record JunexisUser(ulong Id, string Name, string Email);
```

#### Model 2: `extern type` jako opaque handle (referencja natywna)

Gdy obiekt **nie musi przekraczać granicy** — tylko być przekazywany między `extern task` na tej samej platformie:

```junexis
extern type DbContext @csharp("MyApp.Data.AppDbContext")

extern task OpenDb(connStr: Text)                 @server -> DbContext
extern task QueryUsers(db: DbContext, limit: Int) @server -> List<User>
extern task CloseDb(db: DbContext)                @server
```

---

### Zasady granicy platform

| Typ | Przez kanał między platformami | Parametr `extern task` (ta sama platforma) |
|-----|-------------------------------|---------------------------------------------|
| `struct` | ✅ Dozwolone — kompilator serializuje | ✅ Dozwolone |
| `extern type` | ❌ Błąd `E0014: extern type cannot cross platform boundary` | ✅ Dozwolone |
| Typy prymitywne (`Int`, `Text`, …) | ✅ Dozwolone | ✅ Dozwolone |

**Status:** EXPERIMENTAL w 0.2. Mapowanie `extern type` na konkretny typ natywny może się zmienić w 0.3.

---

## CORE: import (D12)

```junexis
// Forma 1: glob — wszystkie publiczne nazwy → unqualified scope
import domain::sensors

// Forma 2: aliased namespace — dostęp przez ns::Name, nic do unqualified scope
import domain::sensors as sen

// Forma 3: selective — tylko wymienione nazwy → unqualified scope
import domain::sensors { SensorData, ProcessedData }

// Forma 4: selective + alias
import domain::sensors { SensorData as SD, ProcessedData }
```

| Forma | Składnia | Scope |
|-------|----------|-------|
| Glob | `import m::path` | wszystkie publiczne → unqualified |
| Aliased | `import m::path as ns` | dostęp przez `ns::Name`, nic unqualified |
| Selective | `import m::path { X, Y }` | tylko `X`, `Y` → unqualified |
| Selective+alias | `import m::path { X as A }` | `A` → unqualified |

**Widoczność:** Wszystkie top-level deklaracje w pliku są **domyślnie publiczne** — brak `export` keyword. Konwencja prywatności: prefiks `_` (nie egzekwowany przez kompilator 0.2).

**Ścieżki modułów:** `::` jako separator (np. `domain::sensors`). Resolution `::` → ścieżka pliku jest zakresem implementacyjnym kompilatora, nie spec.

**Kolizje nazw:** Glob import z kolizją → błąd `E0002`. Rozwiązanie: selective lub aliased.

---

## EXPERIMENTAL (opaque): narrative

`narrative` opisuje metadane biznesowe systemu — intent, compliance, observability. Treść jest opaque w 0.2.

```junexis
narrative "Payment Processing" {
    intent "ensure atomic payment with customer satisfaction"
    compliance "PCI-DSS, GDPR"
    trace full
    stakeholders ["payments-team", "security-team"]
    applies_to { ProcessPayment, RefundOrder }
}
```

**Uwaga:** Parser zapamiętuje cały blok jako string. Kompilator interpretuje
obecnie wyłącznie wąską politykę `trace` potrzebną do instrumentacji M6.1;
semantyka `intent`, `compliance` i pozostałych pól nadal należy do Junexis
Narrative. Kontrakt wykonania opisuje
[`15-narrative-runtime-trace.md`](15-narrative-runtime-trace.md).

---

## EXPERIMENTAL (opaque): deployment

`deployment` mapuje tasks na urządzenia/środowiska:

```junexis
deployment Production {
    TempSensor     @ esp32c3_kitchen
    DataProcessor  @ aws_us_east
    Dashboard      @ vercel_prod

    protocol mqtt {
        broker: "mqtt.example.com"
        port: 1883
    }
}
```

---

## EXPERIMENTAL (opaque): supervisor

`supervisor` definiuje strategię superwizji w stylu Erlang OTP:

```junexis
supervisor SensorSupervisor {
    strategy: one_for_one
    max_restarts: 5
    window: 30s

    children {
        TempSensor(pin: 4)
        HumiditySensor(pin: 5)
    }
}
```

---

## EXPERIMENTAL (opaque): circuit_breaker

`circuit_breaker` konfiguruje software-level circuit breaker:

```junexis
circuit_breaker PaymentGateway {
    failure_threshold: 5
    open_duration: 30s
    success_threshold: 2

    on open {
        log("circuit open — using fallback")
    }
}
```

---

## EXPERIMENTAL (opaque): quirks

`quirks` opisuje obejścia hardware-specific:

```junexis
quirks "ESP32-C3 USB-Serial" {
    applies_to @embedded(esp32c3)

    workaround usb_serial_reset {
        description "USB-Serial requires driver reset on first connect"
        @c/c++ {
            // generated NetworkDebugLogger.cpp
        }
    }
}
```

---

## Zasady kolejności deklaracji

- `import` musi być pierwsze.
- Pozostałe deklaracje mogą występować w dowolnej kolejności.
- `native`, `hardware`, `config` są zagnieżdżone wewnątrz `device` lub `task` — nie są deklaracjami top-level.
- Wielokrotne deklaracje tego samego `task Name` to błąd kompilacji (`E0001: duplicate declaration`).
- Wielokrotne `narrative` / `deployment` są dozwolone — komponują się.
