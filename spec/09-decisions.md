# Junexis Lang — Decyzje projektowe i uzasadnienia (ADR)

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

Dokument zbiera kluczowe decyzje architektoniczne eksperymentalnego języka Junexis Lang 0.2. Każda decyzja opisuje kontekst, rozważane opcje, wybrany kierunek i jego uzasadnienie. Decyzje mogą być korygowane podczas konsolidacji implementacji.

---

## D1 — Communication-First type system

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D1` (zakres: Junexis Lang).
**Powiązanie zakresów:** PLATFORM-ADR-002 — model generowania kodu; ten wpis określa regułę języka.


**Kontekst:** Jak modelować komunikację między komponentami?

**Opcje rozważane:**
- A) Biblioteczne — `Channel<T>`, `Queue<T>` jako zwykłe typy generyczne w bibliotece
- B) Pierwszoklasowe — `chan<T>`, `queue<T>`, `stream<T>` wbudowane w system typów

**Wybrano: B**

**Uzasadnienie:**  
Systemy rozproszone mają **komunikację jako swój rdzeń**, nie jako opcję. Biblioteczne kanały (Go channels, Java BlockingQueue) wymagają od developera decyzji o protokole, serializacji i obsłudze błędów — co powoduje ogromną ilość boilerplate i podatność na błędy.

Gdy typy komunikacyjne są pierwszoklasowe, kompilator może:
1. Automatycznie generować adaptery protokołów między platformami
2. Walidować semantykę (zero-loss vs. at-least-once vs. latest-wins)
3. Generować obsługę reconnect, retry, DLQ bez kodu developera
4. Wykrywać błędy architektoniczne: fanout na `chan<T>`, `queue<T>` bez ACK

**Konsekwencje:**
- Trzy typy zamiast jednego — developer musi wybrać świadomie
- Kompilator musi znać semantykę każdego typu (koszty implementacji)
- Leksykon języka jest wzbogacony (`recv`, `send`, `nack`, `respond`)

---

## D2 — Platform-Aware compilation

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D2` (zakres: Junexis Lang).
**Powiązanie zakresów:** D16 rozdziela obecne backendy i docelowe wykonanie; nie zastępuje lokalnej reguły języka.


**Kontekst:** Jak obsłużyć deployment na wiele platform (embedded, server, browser)?

**Opcje rozważane:**
- A) Conditionalne — `#ifdef ESP32`, `#if TARGET == SERVER`
- B) Osobne pliki per platforma
- C) Adnotacje na deklaracjach (`@embedded`, `@server`, `@report`)

**Wybrano: C**

**Uzasadnienie:**  
Opcja A: (preprocessor) to dobrze znany źródło problemów — warunki się multiplikują, testy są trudne, intent jest ukryty. 
Opcja B wymaga duplikacji definicji.

Opcja C:
Adnotacje `@platform` dokumentują *architektoniczny intent* w kodzie źródłowym. Developer deklaruje "ten task należy do embedded" — kompilator generuje właściwy kod, waliduje możliwości platformy i tworzy mostki między platformami.

**Konsekwencje:**
- Jeden plik `.jnx` może opisywać cały system (embedded + server + frontend)
- Kompilator generuje osobne artefakty per platforma z jednego źródła
- Semantyka adnotacji musi być precyzyjnie zdefiniowana (co wolno, czego nie)

---

## D3 — Opaque metadata blocks (narrative, deployment, supervisor, ...)

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D3` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Język potrzebuje bloków metadanych (narrative, deployment, supervisor, circuit_breaker, quirks). Jak je parsować?

**Opcje rozważane:**
- A) Pełna gramatyka — zdefiniuj każde pole, typ, semantykę
- B) Conservative opaque — parser zbiera balanced braces bez analizy treści
- C) YAML wewnątrz `.jnx`

**Wybrano: B**

**Uzasadnienie:**  
Semantyka bloków takich jak `narrative` (metadane obserwowalne) lub `deployment` (orchestracja) jest złożona i ewoluuje. Zdefiniowanie pełnej gramatyki w 0.2 zamroziłoby decyzje projektowe zbyt wcześnie.

Opaque blocks (balanced-brace capture) pozwalają:
- Kompilatorowi rozpoznać blok i nie emitować błędów składniowych
- Toolingowi (LSP, IDE) pokazać zawartość bez pełnego parsowania
- Przyszłym wersjom zdefiniować pełną semantykę bez breaking change

**Konsekwencje:**
- Kompilator nie waliduje zawartości tych bloków w 0.2
- Błędy w `narrative { ... }` nie są wykrywane przez parser
- IDE autocomplete wymaga dodatkowej warstwy poza gramatyką

Bloki EXPERIMENTAL są wyraźnie oznaczone w spec — developer wie, że semantyka nie jest gwarantowana.

---

## D4 — Brak wyjątków / try-catch-throw

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D4` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Obsługa błędów w języku — wyjątki czy explicit error values?

**Opcje rozważane:**
- A) Wyjątki — `try/catch/throw` jak Java/C#/Python
- B) Result types — union `T | Error` + pattern matching
- C) Panic + recover (Go-style)

**Wybrano: B**

**Uzasadnienie:**  
Systemy rozproszone wymagają **precyzyjnej obsługi błędów** — "coś poszło nie tak" nie wystarczy. Union types (`Result::Ok(value) | Result::Err(Code, msg)`) wymuszają na developerze jawne obsłużenie każdej ścieżki błędu w miejscu wywołania.

Wyjątki są ukrytym przepływem sterowania — funkcja może "wyrzucić" bez żadnego oznaczenia w sygnaturze. W systemach rozproszonych to prowadzi do niezauważonych błędów i niestabilności.

**Konsekwencje:**
- Pattern matching na union types jest podstawowym narzędziem
- `supervisor` (EXPERIMENTAL) obsługuje restart strategies na wyższym poziomie

---

## D5 — Walidacja platform w compile-time

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D5` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Kiedy wykrywać błędy platform (np. task `@embedded` używa WiFi, ale chip go nie ma)?

**Opcje rozważane:**
- A) Runtime — błąd przy starcie
- B) Compile-time — na podstawie profilu platformy
- C) Linting — osobne narzędzie, po kompilacji

**Wybrano: B (z możliwością D4 runtime-check)**

**Uzasadnienie:**  
Błędy platformy wykryte w runtime (po deployu na device) są kosztowne — wymagają reflash, diagnostyki sprzętowej. Kompilator ma profil platformy (YAML lub hardcoded) z informacją o możliwościach.

Walidacja compile-time eliminuje klasę błędów "to zadziała na devbox, ale nie na ESP32-C3 bez WiFi". Diagnostyki `E5001-E5008` i `W5001-W5013` informują developera zanim binarka trafi na sprzęt.

**Konsekwencje:**
- Kompilator musi mieć profile platform (wariant → capabilities)
- Nowe platformy wymagają stworzenia profilu
- False positives są możliwe gdy profil jest niedokładny

---

## D6 — Automatyczne dobieranie protokołu

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D6` (zakres: Junexis Lang).
**Powiązanie zakresów:** D7 dotyczy składni deklaracji, D6 automatycznego doboru; są to odrębne rozstrzygnięcia.


**Kontekst:** Kto wybiera protokół komunikacyjny między platformami?

**Opcje rozważane:**
- A) Developer explicite — `@protocol(mqtt)` wymagane zawsze
- B) Framework — zdefiniowane reguły, brak override
- C) Kompilator z override — domyślnie automatyczny dobór, developer może nadpisać

**Wybrano: C**

**Uzasadnienie:**  
Dobór protokołu to inżynierska decyzja z wieloma zmiennymi (charakterystyka sieci, wolumen, latencja, gwarancje dostarczenia). Kompilator ma dostęp do wszystkich tych informacji i może podjąć "rozsądną domyślną" decyzję.

Developer nie powinien musieć pamiętać, że `@embedded → @server` przez MQTT, a `@server → @report` przez WebSocket. Ale gdy chce zmienić domyślne (np. REST zamiast gRPC bo legacy integration), ma `@protocol(name)`.

**Konsekwencje:**
- Kompilator emituje `I6002` (info) gdy wybiera protokół automatycznie
- Tabela domyślnych protokołów musi być udokumentowana i stabilna
- "Magic" — developer musi wiedzieć co kompilator zrobi

---

## D7 — Protocol jako osobna deklaracja top-level

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D7` (zakres: Junexis Lang).
**Powiązanie zakresów:** PLATFORM-ADR-001 — wspólny model protokołów; D7 zachowuje własność składni.


**Kontekst:** Jak powiązać taski z protokołami komunikacyjnymi (REST, gRPC, MQTT...)?

**Opcje rozważane:**
- A) Adnotacja na tasku — `task ListUsers() @rest(GET "/users") @server -> ...`
- B) Konfiguracja w `config {}` bloku
- C) Osobna deklaracja top-level — `protocol rest { ... }`

**Wybrano: C**

**Uzasadnienie:**  
Task opisuje logikę domenową — nie powinien wiedzieć nic o protokołach. Protokół to binding między logiką a powierzchnią komunikacyjną. Opcja A miesza dwie odpowiedzialności w jednej deklaracji.

Osobna deklaracja top-level pozwala:
- Bindować jeden task do wielu protokołów bez żadnej zmiany w tasku
- Stosować middleware per `protocol { }`, nie per task
- Definiować własne protokoły przez user-defined IR modules

**Konsekwencje:**
- Tokeny `@rest`, `@grpc`, `@ws`, `@mqtt`, `@coap`, `@amqp` NIE ISTNIEJĄ w leksykonie
- Keyword `protocol` jest top-level deklaracją (CORE)
- Binding przez opaque body wewnątrz `protocol <name> { }` — interpretowany w IR stage

---

## D8 — `let` jako domyślna niezmienność

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D8` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Jaki default dla zmiennych — mutowalne czy niemutowalne?

**Opcje rozważane:**
- A) Mutowalne domyślnie (C, Java) — `int x = 0; x = 1`
- B) Niemutowalne domyślnie (Rust, Swift) — `let x = 0; x = 1 // error`
- C) Trzy słowa kluczowe: `let`, `const`, `var`

**Wybrano: C**

**Uzasadnienie:**  
`const` dla kompile-time stałych, `let` dla niezmiennych locals (inicjalizacja raz), `var` dla mutowalnych. Kontrakt jest jasny i explict.

`let` jako default zachęca do stylu funkcyjnego (immutability jest bezpieczniejsza w concurrent/distributed). `var` istnieje gdy naprawdę potrzebny (`state counter = counter + 1`). `const` dla wartości znanych w compile-time.

**Konsekwencje:**
- Trzy słowa kluczowe zamiast jednego (więcej do nauki)
- `let x = ...; x = ...` to błąd kompilacji — niepokojące dla przyzwyczajonych do JS `let`
- `state` jest kolejnym słowem — ale z wyraźnie inną semantyką (persistent)

---

## D9 — Brak GC w `@embedded` generowanym kodzie

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D9` (zakres: Junexis Lang).
**Powiązanie zakresów:** D16 rozdziela obecne backendy i docelowe wykonanie; nie zastępuje lokalnej reguły języka.


**Kontekst:** Embedded devices (ESP32, STM32) nie mają podmiotowego Garbage Collectora.

**Opcje rozważane:**
- A) GC runtime — mikro-GC w wygenerowanym C++
- B) Static allocation — wszystko na stosie lub w BSS
- C) Arena allocator — per-task arena

**Wybrano: B (static allocation first, arena jako opcja)**

**Uzasadnienie:**  
GC w embedded to źródło niedeterminizmu — pauzy GC mogą uderzyć w real-time wymagania. Static allocation jest przewidywalna i audytowalnie bezpieczna.

Kompilator generuje rozmiary buforów statycznie na podstawie:
- `storage_budget` w `config`
- Rozmiaru typów w `chan<T>` / `queue<T>`
- Liczby tasków i ich stack sizes

**Konsekwencje:**
- Kompilator musi znać rozmiary statycznie — brak dynamicznie rosnących struktur
- Developer musi jawnie deklarować limity dla `queue<T>` (`storage_budget`)
- Upraszcza audyt bezpieczeństwa (brak heap fragmentation)

---

## D10 — Gramatyka ANTLR4 jako kanon

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D10` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Jak zdefiniować formalną gramatykę języka?

**Opcje rozważane:**
- A) BNF/EBNF w dokumentacji — reference grammar
- B) PEG parser (peg.js, tree-sitter)
- C) ANTLR4 — parser generator z visitor generation

**Wybrano: C**

**Uzasadnienie:**  
ANTLR4 to sprawdzony tool dla kompilatorów produkcyjnych. Generuje:
- Lexer i parser w C# (docelowy język kompilatora)
- Visitor pattern (AST traversal)
- Error recovery

Plik `JunexisLang.g4` jest **kanonicznym źródłem prawdy** dla gramatyki — wszelkie rozbieżności między dokumentacją a `.g4` are resolved by `.g4`.

**Konsekwencje:**
- `.g4` musi być zawsze aktualny — spec zmiana → zmiana `.g4` → zmiana spec
- ANTLR4 jest dependency narzędzi deweloperskich (nie runtime)
- Tree-sitter bindings mogą być generowane z ANTLR4 gramatyki (nie vice versa)

---

## D11 — Progressive Disclosure (poziomy szczegółowości)

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D11` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Jak pogodzić prostotę dla początkujących z możliwościami dla zaawansowanych?

**Opcje rozważane:**
- A) Jeden poziom — pełna konfiguracja zawsze
- B) Dwie wersje języka — "beginner" i "advanced"
- C) Progressive Disclosure — każda warstwa jest opcjonalna

**Wybrano: C**

**Uzasadnienie:**  
`task TempSensor() @embedded -> chan<TempData>` to działający system. Bez `config`, bez `device`, bez `protocol`. Kompilator wypełnia sensowne domyślne.

Każda warstwa szczegółowości jest opcjonalna i addytywna:
- Level 1: sam deployment (jeden plik, kilka linii)
- Level 2: `device` + `config` + `protocol`
- Level 3: pełna konfiguracja (`native`, `quirks`, `supervisor`, `circuit_breaker`)

**Konsekwencje:**
- Kompilator musi mieć solidne domyślne wartości
- Dokumentacja musi wyraźnie oddzielać poziomy
- Ryzyko: "magic" decyzji kompilatora — nieprzewidywalne dla eksperta

---

## D12 — System modułów: `import` z `::`, implicit public

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D12` (zakres: Junexis Lang).
**Powiązanie zakresów:** Decyzja lokalna języka; brak wskazanego następcy całej decyzji.


**Kontekst:** Jak Junexis Lang obsługuje wieloplikowe systemy — jak typy i taski z jednego pliku są dostępne w innym?

**Opcje rozważane:**
- A) Brak modułów — jeden plik = jeden system (MVP)
- B) File-based imports — `import "./sensors.jnx"` (ścieżki plików)
- C) Name-based `::` + implicit public — `import domain::sensors`, wszystkie publiczne nazwy dostępne
- D) Export-gated — explicit `export` keyword, bez niego niewidoczne (jak Rust `pub`)

**Wybrano: C**

**Uzasadnienie:**  
`::` jest już operatorem scope resolution (variant access: `Result::Ok`). Name-based moduły są spójną extensją — kontrakt językowy mówi o *nazwie* modułu, nie o lokacji pliku. Ścieżki plików (`"./..."`) są szczegółem implementacyjnym kompilatora.

Brak `export` keyword: wszystkie top-level deklaracje są publicznie importowalne. Systemy Junexis są małe i targetowane — explicit visibility control jest zbędne w 0.2. Konwencja `_name` dla "prywatnych" (nie egzekwowane).

Cztery formy `import` pokrywają wszystkie przypadki przy użyciu tylko istniejących tokenów (`AS`, `LBRACE`, `RBRACE`). `FROM` NIE jest używany w `import` — unikamy konfuzji z `recv from channel`.

**Składnia:**

```junexis
import domain::sensors                       // glob → unqualified scope
import domain::sensors as sen                // aliased → sen::SensorData
import domain::sensors { SensorData }        // selective → unqualified
import domain::sensors { SensorData as SD }  // selective + alias
```

**Konsekwencje:**
- Glob import z kolizją → błąd `E0002` (rozwiązanie: selective lub aliased)
- Resolution `::` → plik jest zakresem implementacyjnym kompilatora (nie spec)
- Brak `export`, brak per-symbol visibility — 0.3 może dodać gdy będzie use-case
- `import` musi być pierwsze w pliku (przed deklaracjami top-level)

---

## D13 — Integracja kodu natywnego i zewnętrznych pakietów

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D13` (zakres: Junexis Lang).
**Powiązanie zakresów:** PLATFORM-ADR-002 — model generowania kodu; ten wpis określa regułę języka.


**Doprecyzowanie zakresu (2026-09-24):** poniższy `junexis.toml` i sekcje `[deps.*]` są historycznym szkicem integracji zależności natywnych, nie kontraktem obsługiwanym przez bieżący manifest projektu. Kanonicznym manifestem projektu jest `junexis.project.toml` (D18, `spec/14-project-layout.md`). Nie należy kopiować szkicu `[deps.*]` do tego manifestu ani zakładać automatycznej obsługi zależności Go/NPM/NuGet. Lista języków w kontekście obejmuje rozważane integracje; obecne backendy bootstrapowe określa D16.

**Kontekst:** Junexis generuje kod w C++, Go, TypeScript, C#. Jak developer korzysta z bibliotek tych języków — np. `github.com/redis/go-redis`, `mongoose`, `OpenCV`?

**Problem ma trzy odrębne warstwy.**

**Warstwa 1 — stdlib i wygenerowane importy:** Auto-generowane przez codegen. Zero działania w .jnx.  
`protocol rest { }` → Go dostaje `import "net/http"`. `chan<T> @server` → Go generuje `import "sync"`. Developer nigdy tego nie dotyka.

**Warstwa 2 — biblioteki zewnętrzne:**
- `@embedded` tasks: sekcja `native { @c/c++ { ... } }` wewnątrz `task` (spójna z `device.native`). Strukturyzowana, parsowana, w .jnx.
- `@server` / `@report` tasks: plik `junexis.toml` — manifest narzędzi budowania, poza .jnx.

Podział wynika z natury platform: PlatformIO/CMake dla embedded ma swój własny format bibliotek — `.jnx` może to opisać strukturalnie. Go Modules, NPM, NuGet są zbyt różne, żeby je modelować w gramatyce języka.

**Warstwa 3 — wywołanie kodu natywnego (FFI):**  
`extern task` deklaruje task z implementacją w języku natywnym. .jnx zna sygnaturę i typy; kompilator generuje bridge i waliduje zgodność.

**Opcje rozważane:**
- A) Wszystko w .jnx — `extern`, bloki `native { }`, inline package declarations
- B) Podział: `reference` dla embedded, `junexis.toml` dla server/report, `extern task` jako FFI
- C) W pełni transparentne — compiler handles all, developer używa tylko .jnx abstrakcji

**Wybrano: B**

**Uzasadnienie:**  
Junexis opisuje *co* system robi, nie *jak go zbudować*. Package management jest concernem narzędzi budowania, nie języka. Wyjątki:
- Embedded libraries są częścią specyfikacji *urządzenia/tasku* (pin, driver, HAL) — logicznie należą do .jnx, bo wpływają na generowany kod C++.
- `extern task` jest minimalnym, jawnym escape hatch — developer świadomie wykracza poza Junexis. Granica jest wyraźna.
- Opcja A sprawia, że .jnx staje się polglot chaosem i duplikuje systemy pakietów. Opcja C jest nierealistyczna dla 0.2 — Junexis nie ma abstrakcji dla wszystkich możliwych operacji natywnych.

**Wzorzec `junexis.toml`:**

```toml
# junexis.toml — plik w katalogu projektu, obok .jnx

[deps.go]
"github.com/redis/go-redis/v9" = "v9.5"
"github.com/lib/pq"            = "^1.10"

[deps.npm]
"pg"       = "^8.0"
"mongoose" = "^8.0"

[deps.csharp]
"Npgsql" = "8.0"
```

**Konsekwencje:**
- `extern task` jest EXPERIMENTAL w 0.2 — interfejs bridge (konwencja prefixu, mapowanie typów) może się zmienić w 0.3
- `native` w `task` jest CORE — spójne z `device.native` (D14)
- `junexis.toml` nie jest definiowany przez gramatykę .jnx — zakres implementacyjny toolchaina
- Kod server/report z dependencjami: `extern task` deklaracja + `junexis.toml` + plik natywny (3 elementy)

---

## D14 — `native` zamiast `reference` — nazwa sekcji zależności natywnych

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D14` (zakres: Junexis Lang).
**Powiązanie zakresów:** PLATFORM-ADR-002 — model generowania kodu; ten wpis określa regułę języka.


**Kontekst:** Sekcja wewnątrz `task` i `device` opisująca zależności i inicjalizacje dla docelowych języków natywnych (C++, Rust, Go…). Jak ją nazwać?

**Opcje rozważane:**
- A) `reference` — pierwotna nazwa v0.x; koliduje z powszechnym znaczeniem w IT: C++ `int&`, project references (`.csproj`), bibliographic references
- B) `native` — znane z Rust/Kotlin/Java (`native` method, `extern "C"`); sygnalizuje "tu wychodzimy poza Junexis"
- C) `binding` / `bindings` — precyzyjne technicznie, rzadziej spotykane jako keyword w językach
- D) `libs` — proste, ale nie oddaje `include`, `init`, `use` — zakres wykracza poza samą listę bibliotek

**Wybrano: B — `native`**

**Uzasadnienie:**  
`native` wyraźnie komunikuje intencję: blok opisuje to, co Junexis zleca natywnemu ekosystemowi platformy. Termin ma ugruntowane znaczenie ("kod poniżej poziomu języka") w Java, Kotlin, Rust, C#. Opcja A (`reference`) budziła konfuzję między typem referencyjnym, referencją do projektu a konfiguracją natywnej biblioteki.

**Zmiany w gramatyce:**  
Token `REFERENCE : 'reference'` → `NATIVE : 'native'`. Sub-reguły: `referenceSection → nativeSection`, `languageReference → nativeLanguage`, `referenceField → nativeField`, `referenceValue → nativeValue`, `referenceFieldList → nativeFieldList`. Semantyka bez zmian.

**Konsekwencje:**
- Breaking change gramatyki: kod v0.x z `reference { }` nie parsuje się w 0.2
- D13 zaktualizowany: `@embedded` używa `native { @c/c++ { ... } }`
- `extern task` (D13) pozostaje bez zmian — dotyczy osobnej warstwy FFI

---

## D15 — Lifecycle hooks: `on` + identyfikator zdarzenia

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D15` (zakres: Junexis Lang).
**Powiązanie zakresów:** PLATFORM-ADR-002 — model generowania kodu; ten wpis określa regułę języka.


**Kontekst:** Tasks muszą reagować na zdarzenia cyklu życia (uruchomienie, połączenie, rozłączenie). Jak to zapisać w gramatyce?

**Opcje rozważane:**
- A) Złożone tokeny — `ON_START : 'on_start'`, `ON_CONNECT : 'on_connect'` itd. — każde zdarzenie osobnym tokenem lekserskim
- B) Keyword `on` + identyfikator — `ON IDENTIFIER LBRACE ... RBRACE` — dwa tokeny, walidacja w IR stage
- C) Bloki jak `every` — osobna składnia per zdarzenie z osobnymi słowami kluczowymi

**Wybrano: B**

**Uzasadnienie:**  
Opcja A zamraża zbiór zdarzeń w lekserze — dodanie `on error` lub `on timeout` w 0.3 wymaga zmiany gramatyki i regeneracji parsera. Opcja B przenosi walidację do IR stage: parser akceptuje `on <dowolny_identyfikator>`, kompilator sprawdza czy identyfikator jest zdarzeniem spełnionym w danym kontekście tasku.

Opcja C (dedykowane słowa kluczowe jak `start_hook { }`, `connect_hook { }`) jest rozwlekła i nieekspandowalna.

`on` jako keyword + IDENTIFIER jest idiomem rozpoznawalnym — Kotlin (`on error`), Elixir callbacks, event-driven frameworks.

**Konsekwencje:**
- Gramatyka: jedna reguła `lifecycleHook : ON IDENTIFIER LBRACE statement* RBRACE`
- Lekser: jeden token `ON : 'on'` — bez `ON_START`, `ON_CONNECT` itd.
- Walidacja: IR stage emituje `E0013` gdy identyfikator nie jest zdarzeniem dostępnym w danym kontekście
- Extendowalność: 0.3 może dodać `on error`, `on timeout` bez zmiany gramatyki
- Zdarzenia wbudowane 0.2: `start`, `connect`, `reconnect`, `disconnect`

---

## D16 — Hybrydowy model wykonania i backendy bootstrapowe

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D16` (odsyłacz zgodności).
**Status:** DIRECTIONAL / EXPERIMENTAL
**Źródło kanoniczne:** dokumentacja architektoniczna Junexis Platform, poza zakresem tego repozytorium.

Kierunek hybrydowy: embedded AOT, server VM z opcjonalnym AOT, report WASM. C++, Go i TypeScript pozostają backendami bootstrapowymi; model docelowy nie jest dowodem implementacji.

Pełny kontekst, warianty, uzasadnienie i konsekwencje przeniesiono 2026-09-24
bez zmiany znaczenia decyzji. D16 pozostaje historycznym identyfikatorem tej samej decyzji.

---

## D17 — `resource`: stan i I/O jako odrębna odpowiedzialność

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D17` (zakres: Junexis Lang).
**Powiązanie zakresów:** D21 rozwija granicę dostawców; nie oznacza przyjęcia ani implementacji całego resource z D17.


> **Status:** DIRECTIONAL / EXPERIMENTAL
> **Zakres 0.2:** model projektowy; brak słowa kluczowego, gramatyki, AST, IR i codegenu

**Kontekst:** Taski muszą pracować z bazami danych, plikami, pamięcią, magazynami
obiektowymi i sekwencyjnym I/O. Modelowanie każdego z nich jako specjalnego
tasku miesza zachowanie ze stanem. Ukrywanie ich wyłącznie za `extern task`
usuwa z modelu systemu trwałość, transakcje, pojemność i granice awarii.

Jednocześnie słowo „stream” ma dwa różne znaczenia: relację komunikacyjną
między częściami systemu oraz sekwencyjny odczyt/zapis danych. ZIP, GZIP, plik
i bufor pamięci nie mają semantyki komunikacyjnego `stream<T>`.

**Opcje rozważane:**

- A) Wyłącznie `extern task` — każda baza i operacja plikowa jest zwykłym FFI.
- B) Specjalne deklaracje `database`, `file`, `memory`, `zip` — osobna konstrukcja dla każdej technologii.
- C) Jedna abstrakcja `resource` opisująca wymagane możliwości; provider jest wiązany przez deployment.
- D) Rozszerzenie komunikacyjnego `stream<T>` o warianty plikowe, pamięciowe i kompresowane.

**Wybrano kierunek C. Odrzucono D jako pomieszanie dwóch semantyk.**

### Podział odpowiedzialności

| Element | Odpowiedzialność |
|---------|------------------|
| `task` | Przetwarza dane i deklaruje potrzebne możliwości. |
| `resource` | Opisuje logiczny dostęp do stanu lub I/O i wymagane gwarancje. |
| `deployment` | Wiąże resource z konkretnym providerem, lokalizacją i sekretami. |
| `protocol` | Opisuje przekroczenie granicy platformy, jeśli dostęp nie jest lokalny. |
| `pipeline` | Pokazuje przepływ danych pomiędzy taskami; nie zastępuje operacji storage. |
| `narrative` | Rejestruje istotne operacje, wynik, przyczynę i granicę pewności. |
| `extern` | Implementuje adapter lub operację niewyrażalną jeszcze w modelu resource. |

### Konsekwencje

- Task nie powinien zależeć od marki bazy lub ścieżki produkcyjnego katalogu.
- Resource nie jest globalnym uchwytem bez lifecycle; ma właściciela, zakres i budżet.
- Binding providera oraz sekrety należą do deploymentu, nie do kodu tasku.
- Implementacja pamięciowa może zastąpić produkcyjną tylko wtedy, gdy spełnia
  wymagane gwarancje; „in-memory” nie oznacza automatycznie równoważności.
- SQL może początkowo występować w adapterze, ale nie staje się przez to częścią
  rdzenia Junexis ani kanonicznym modelem zapytań.
- I/O będzie projektowane jako typowane źródła, ujścia i transformacje. Nazwy
  `Source<T>` / `Sink<T>` są robocze, nie przyjętą składnią.
- ZIP jest formatem archiwum, GZIP transformacją kompresji, a memory buforem lub
  providerem; nie są równorzędnymi „rodzajami streamu”.
- Pierwsza implementacja powinna objąć wąski vertical slice memory + filesystem,
  zanim język otrzyma szeroki katalog adapterów bazodanowych.

Pełny model projektowy, przykłady pseudokodu i otwarte pytania znajdują się w
`spec/10-resources-and-io.md`.

---

## D18 — Project-first workflow i tryb plikowy jako compatibility/playground

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D18` (zakres: Junexis Lang).
**Powiązanie zakresów:** PLATFORM-ADR-002 — odsyłacz do manifestu projektu; kontrakt projektu pozostaje w spec/14-project-layout.md.


> **Status:** ACCEPTED / EXPERIMENTAL
> **Zakres 0.2:** zasada organizacji narzędzi i dokumentacji; nie zmienia
> składni języka

**Kontekst:** Junexis powstał z prototypu jednoplikowego, ale docelowo opisuje
całe systemy rozproszone: wiele platform, artefaktów, deploymentów, protokołów,
diagnostyk i cache narzędziowego. Wspólny katalog `out/` dla wielu przykładów
zaczął mieszać artefakty i utrudniać odpowiedź na podstawowe pytanie:
„co należy do którego systemu?”.

Jednocześnie repozytorium zawiera płaski korpus przykładów i pliki gramatyczne,
które są potrzebne do szybkiej regresji parsera, AST, IR i codegenu. Ich
natychmiastowe usunięcie albo wymuszenie manifestu zwiększyłoby koszt pracy
bez stabilizacji samego języka.

**Opcje rozważane:**

- A) Usunąć tryb pojedynczego pliku i wymagać `junexis.project.toml` zawsze.
- B) Utrzymać tryb pojedynczego pliku jako równorzędny, docelowy workflow.
- C) Przyjąć model project-first: projekt jest docelową jednostką pracy,
  a tryb pojedynczego pliku zostaje jako compatibility/playground/regression
  corpus.

**Wybrano: C**

### Zasada

Docelową jednostką pracy Junexis jest **projekt systemu**, nie samotny plik
źródłowy:

```text
my-system/
  junexis.project.toml
  src/
    main.jnx
  out/
  .junexis/
```

Tryb pojedynczego pliku pozostaje dozwolony wyłącznie jako:

- szybki eksperyment i minimalny repro,
- corpus gramatyki i parsera,
- regresja AST/IR/codegen,
- kompatybilność z przykładami prototypu.

Nowe przykłady reprezentujące realny system powinny być dodawane jako projekty.
Płaskie `examples/*.jnx` mogą pozostać jako corpus regresyjny, ale nie powinny
być traktowane jako wzorzec organizacji docelowych systemów.

### Zasady ochronne

- Dokumentacja i komunikaty CLI powinny mówić językiem **project-first**.
- Jeśli tryb plikowy generuje artefakty, jego output musi być izolowany per
  plik albo jawnie wskazany przez `--out`.
- Zmiana zachowania CLI dotycząca projektu, manifestu, `out/` albo `.junexis/`
  wymaga aktualizacji:
  - `spec/14-project-layout.md`,
  - `spec/12-cli-and-lsp.md`,
  - `spec/STATUS.md`,
  - tej decyzji, jeśli zmienia zasadę.
- Każdy agent lub kontrybutor powinien sprawdzić `spec/09-decisions.md` przed
  dodaniem alternatywnego workflow.
- Legacy/compatibility nie oznacza „równorzędny kierunek rozwoju”.

**Konsekwencje:**

- CLI może nadal obsługiwać `junexis check file.jnx` i `junexis build file.jnx`,
  ale nie wolno na tej podstawie projektować kolejnych funkcji jako file-first.
- `junexis init` i `junexis.project.toml` stają się główną ścieżką pracy dla
  nowych systemów.
- VS Code extension i przyszłe IDE powinny wykrywać najbliższy manifest projektu
  i traktować go jako kontekst komend.
- Udany build projektu publikuje atomowy, wersjonowany
  `.junexis/state/latest-build.json`; file-mode używa izolowanego
  `.junexis/state/<plik>/latest-build.json`. Narzędzia nie powinny wyznaczać
  aktywnego builda przez skanowanie przypadkowych katalogów wyjściowych.
- `clean` usuwa `.junexis/state/` razem z outputem, aby trwały wskaźnik nie
  prowadził do usuniętych artefaktów.
- Migracja przykładów może być stopniowa: najpierw jeden reprezentatywny projekt,
  później kolejne, bez usuwania płaskiego corpus regresyjnego.
- Jeżeli dokumentacja nie nadąża za implementacją, status musi zostać obniżony
  albo oznaczony jako `UNVERIFIED`/`PARTIAL`, zamiast utrzymywać fikcyjną pewność.

---

## D19 — Referencyjny SQLite bez CGO

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D19` (zakres: Junexis Lang).
**Powiązanie zakresów:** Wybór sterownika dotyczy historycznego pionu generowanego Go z M11.8, nie wszystkich dostawców SQLite. D21 określa późniejszą granicę niezależnego dostawcy; obecny dostawca .NET używa Microsoft.Data.Sqlite.


**Status:** Przyjęta (2026-08-14)
**Kontekst:** M11.8, D17

Referencyjny provider storage dla `@server` musi dać się zbudować na Windows x64
bez instalowania kompilatora C. Jednocześnie powinien korzystać ze standardowego
API `database/sql`, zachować transakcje SQLite i tworzyć samodzielne binarium Go.

**Wybrano:** przypięty `modernc.org/sqlite v1.56.0`, generowany pakiet
`junexis/storage/sqlite` oraz test z `CGO_ENABLED=0`.

Zasady:

- zależność pojawia się tylko wtedy, gdy program deklaruje `extern storage`,
- taki artefakt deklaruje Go 1.25, zgodnie z wymaganiem wybranej wersji drivera,
- zmiana wersji drivera wymaga ponownego przejścia natywnego testu M11.8,
- adapter domenowy nie importuje bezpośrednio drivera; korzysta z API providera,
- migracje i wartości zapytań są wykonywane przez kontrolowaną warstwę,
- `github.com/mattn/go-sqlite3` nie jest providerem referencyjnym, ponieważ
  wymaga CGO.

Źródła decyzji: [repozytorium drivera](https://gitlab.com/cznic/sqlite) oraz
[moduł przypiętej wersji](https://gitlab.com/cznic/sqlite/-/raw/v1.56.0/go.mod).

**Konsekwencje:** zwykły projekt bez storage zachowuje dotychczasowe `go 1.22`.
Projekt SQLite wymaga toolchainu Go 1.25+ podczas budowania ze źródeł, ale gotowa
paczka aplikacji nie wymaga Go ani toolchainu C na komputerze użytkownika.

---

## D20 — Wspólna granica domenowa i niezależny provider memory

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D20` (zakres: Junexis Lang).
**Powiązanie zakresów:** Wspólna semantyka memory i SQLite pozostaje zasadą zgodności. Interfejs storage.Store opisuje pion Go z M11.9; publiczną granicę niezależnego dostawcy rozwijają D21, D24 i SDK.


**Status:** Przyjęta (2026-08-14)
**Kontekst:** M11.9, D17, D19

Test logiki domenowej nie powinien wymagać pliku SQLite, ale provider testowy nie
może zmieniać znaczenia operacji ani udawać trwałości. SQLite uruchomione jako
`:memory:` nadal testowałoby ten sam driver i nie dawałoby niezależnego dowodu
zgodności drugiej implementacji.

**Wybrano:** wspólny interfejs `storage.Store` oraz niezależny provider memory z
mapą defensywnych kopii i transakcją copy-on-write.

Zasady:

- adapter domenowy zależy od `storage.Store`, nie od pakietu SQLite,
- wspólne operacje to `Put`, `Get`, `List`, `Delete`, `Transact`, `Health` i
  `Close`,
- SQLite może dodatkowo wystawiać eksperckie `Exec`, `Query` i jawny `Begin`,
- memory zaczyna puste i traci stan przy `Close`,
- commit memory publikuje pełną kopię atomowo i wykrywa konflikt wersji,
- ten sam test zgodności musi być uruchamiany dla obu providerów.

**Konsekwencje:** neutralny rekord 0.1 używa `bucket`, `key` i `payload`; jest to
granica runtime adaptera, nie nowa składnia ani rezygnacja z typów Junexis.
Docelowy codegen mapowania typów pozostaje częścią M11.10.

---

## D21 — Provider Runtime niezależny od targetu i backendu

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D21` (odsyłacz zgodności).
**Status:** Accepted (2026-08-21)
**Źródło kanoniczne:** dokumentacja architektoniczna Junexis Platform, poza zakresem tego repozytorium.

Dostawca jest niezależnym pakietem używającym wersjonowanego ABI. Target, backend, storage, dostawca i sterownik mają odrębne odpowiedzialności; generator nie zawiera implementacji konkretnej bazy.

Pełny kontekst, warianty, uzasadnienie i konsekwencje przeniesiono 2026-09-24
bez zmiany znaczenia decyzji. D21 pozostaje historycznym identyfikatorem tej samej decyzji.

---

## D22 — Logiczny model danych i Query Plan IR przed loweringiem providera

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D22` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język definiuje logiczny model i algebrę odczytu; D24 określa granicę wykonania, D25–D29 interpretację i optymalizację u dostawcy.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.1, D17, D21

Automatyczne mapowanie danych nie może polegać na generowaniu SQL bezpośrednio
ze składni języka. Taka droga związałaby semantykę programu z dialektem jednej
bazy i utrudniła wspólne testowanie providerów memory, SQLite i PostgreSQL.

**Wybrano:** dwa jawne, wersjonowane kontrakty kompilatora:

- `junexis.storage.model/0.1` opisuje logiczne encje, pola, nullability i klucz;
- `junexis.storage.query-plan/0.1` opisuje przenośną algebrę operacji na danych.

Pierwszy pion algebry obejmuje wyłącznie odczyt: `scan`, `filter`, `project`,
`sort` i `limit`. Parametry są oddzielone od struktury planu, a walidator śledzi
typ i kształt wyniku po każdym węźle. SQL, fizyczne nazwy tabel, indeksy i wybór
providera należą do późniejszego loweringu.

**Konsekwencje:** M13.1 tworzy fundament dla abstrakcyjnego ORM-u, ale nie jest
jeszcze ORM-em dostępnym ze składni Junexis. Mutacje, relacje, agregacje, joiny,
projekcja z AST oraz wykonywanie przez providery są oddzielnymi etapami M13.

---

## D23 — Jawna deklaracja `storage` projektowana do neutralnego IR

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D23` (zakres: Junexis Lang).
**Powiązanie zakresów:** Składnia i projekcja do IR należą do języka. D22 definiuje model; D24 opisuje wywołanie publicznego ABI. SQL i wybór dostawcy pozostają poza składnią.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.2, D21, D22

Model i Query Plan IR z M13.1 potrzebują reprezentacji dostępnej dla autora
programu, ale składnia nie może ujawniać SQL, drivera ani backendu targetu.
Niejawne rozróżnianie parametru i pola prowadziłoby ponadto do zmiany znaczenia
zapytania po rozbudowie encji.

**Wybrano:** ustrukturyzowaną deklarację `storage`, zawierającą logiczne
`entity` i nazwane `query`. Pierwszy podzbiór jest tylko do odczytu i zachowuje
kolejność `filter`, `project`, `sort`, `limit`. Pole jest zapisywane zwykłą
nazwą, a parametr zawsze przez `params.<nazwa>`.

Zasady:

- parser, AST i Structural IR zachowują strukturę oraz lokalizację autora;
- osobny projektor tworzy kontrakty M13.1 i uruchamia ich walidator;
- typy są ograniczone do przenośnego modelu Storage ABI 0.1;
- kroki zmieniają kształt danych w zapisanej kolejności;
- nieprzenośne wyrażenie jest błędem kompilatora, nie zadaniem providera;
- słowa M13 są kontekstowe, aby zachować poprawność istniejącego
  `extern storage`, parametrów `limit` i podobnych nazw;
- wybór providera, fizyczny schemat, SQL i wykonanie pozostają poza deklaracją.

**Konsekwencje:** Junexis ma pierwszy kompilowalny, abstrakcyjny model odczytu
danych, ale jeszcze nie wykonuje go. Mutacje, relacje, agregacje, binding tasku
i provider lowering wymagają osobnych decyzji oraz testów pionowych.

---

## D24 — Query Plan korzysta z istniejącego Storage Runtime ABI

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D24` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje publiczny kontrakt wywołania query.plan. Język odpowiada za walidację i lowering z D22–D23, Runtime za granicę D25; interpretacja należy do dostawcy.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.3, D21, D22, D23

Query Plan potrzebuje granicy wykonawczej, ale osobny transport dla zapytań
powieliłby lifecycle, binding, deadline, kursor i klasy błędów z M12.
Generowanie SQL w kompilatorze ponownie związałoby intencję z providerem.

**Wybrano:** istniejące `invoke` kontraktu
`junexis.storage.runtime/0.1`, z operacją `query.plan`, efektem `read` i
capability `query-plan`. Wersjonowane wejście
`junexis.storage.query-invocation/0.1` przenosi model, uporządkowane kroki,
argumenty i końcowy shape.

Zasady:

- lowerer uruchamia walidację M13.1 przed utworzeniem deskryptora;
- argumenty są typowane, kompletne i normalizowane do kanonicznych wartości;
- kolejność wejściowa argumentów nie wpływa na JSON ani fingerprint;
- deskryptor nie zawiera bindingu, sekretów, request ID ani timeoutu;
- wire używa jawnych tekstowych dyskryminatorów, nie nazw klas i enumów C#;
- SQL, fizyczny schemat i wybór providera nie należą do kompilatora;
- samo utworzenie deskryptora nie jest dowodem wykonania przez provider.

**Konsekwencje:** wszystkie backendy targetów mogą wysłać ten sam zamiar
przez klienta M12. Provider musi jednak jawnie zadeklarować i zaimplementować
`query-plan`; pierwsze wykonanie oraz conformance pozostają kolejnym etapem.

---

## D25 — Provider interpretuje Query Plan, Host tylko chroni granicę

**Właściciel merytoryczny:** `junexis-runtime`. **Identyfikator źródłowy:** `D25` (zakres: Junexis Lang).
**Powiązanie zakresów:** Runtime utrzymuje kontrolę capability na granicy Hosta; dostawca waliduje i interpretuje plan. D26 rozwija wspólne wykonanie referencyjne poza Hostem.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.4, D21, D22, D24

**Wybrano:** Host rozpoznaje `query.plan` wyłącznie po to, aby wymagać
capability `query-plan`. Walidacja i interpretacja algebry należy do providera.
Pierwszy pion referencyjny wykonuje provider memory na snapshotcie bucketu o
nazwie encji i zwraca typowany kursor.

**Konsekwencje:** Host nie staje się ORM-em, provider nie zależy od kompilatora,
a SQLite i PostgreSQL mogą później wykonać dokładnie tę samą kopertę własnym
loweringiem. Mapowanie bucket = encja jest dowodem memory, nie częścią
uniwersalnego kontraktu fizycznego.

---

## D26 — Wspólna semantyka referencyjna, osobny fizyczny scan providera

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D26` (zakres: Junexis Lang).
**Powiązanie zakresów:** Wspólny kernel jest implementacją referencyjną dostawców, nie częścią Hosta ani kompilatora. SDK określa zgodność publicznego kontraktu, a D27 granicę optymalizacji.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.5, D21, D22, D25

**Wybrano:** referencyjne providery .NET memory i SQLite współdzielą mały
kernel walidacji oraz interpretacji Query Plan 0.1. Każdy provider nadal sam
wybiera dane, transakcję, budżety i lifecycle kursora. Kernel nie trafia do
Hosta ani kompilatora.

**Konsekwencje:** drugi provider nie może przypadkowo nadać innego znaczenia
predykatom, null, projekcji lub sortowaniu. Zewnętrzne providery mogą użyć
własnej implementacji, lecz muszą przejść wspólną bramę conformance. M13.5
materializuje ograniczony snapshot SQLite; SQL pushdown pozostaje osobną
optymalizacją fizyczną i nie zmieni kontraktu języka.

---

## D27 — Pushdown tylko dla dowiedzionego podzbioru semantyki

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D27` (zakres: Junexis Lang).
**Powiązanie zakresów:** Dostawca odpowiada za fizyczną optymalizację i zgodny fallback. Nie zmienia algebry D22 ani publicznego ABI D24.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.6, D22, D25, D26

**Wybrano:** provider może obniżyć Query Plan do natywnego języka magazynu
wyłącznie wtedy, gdy zachowuje dokładną semantykę wspólnego kontraktu. Dane
użytkownika muszą być parametrami sterownika. Fragmenty nieobsługiwane lub
semantycznie różne powodują wykonanie całego planu przez zgodny fallback.

**Konsekwencje:** optymalizacja fizyczna nie staje się nową cechą języka.
SQLite może rozwijać własny lowerer bez zależności kompilatora i Hosta od SQL.
M13.6 świadomie nie obniża nullable sortu ani typów, których porządek nie ma
jeszcze dowodu zgodności. Preflight i natywny SQL widzą jeden snapshot.

---

## D28 — Trzeci provider najpierw współdzieli semantykę, potem optymalizuje

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D28` (zakres: Junexis Lang).
**Powiązanie zakresów:** Etap zgodnego wykonania PostgreSQL poprzedza optymalizację D29. Historyczny wynik próby na bazie ma własną datę i nie jest dowodem każdego późniejszego wydania.


**Status:** Przyjęta (2026-08-22)
**Kontekst:** M13.7, D25, D26, D27

**Wybrano:** PostgreSQL najpierw wykonuje pełny Query Plan przez wspólny kernel
na ograniczonym snapshotcie. Natywny SQL pushdown PostgreSQL nie jest warunkiem
przenośności i pozostaje osobnym krokiem.

**Konsekwencje:** trzeci backend otrzymuje dokładnie tę samą semantykę przed
optymalizacją. Status zweryfikowany wymagał rzeczywistego serwera PostgreSQL i
przejścia wspólnej bramki live; warunek został spełniony 23 sierpnia 2026 na
PostgreSQL 17.5.

---

## D29 — PostgreSQL pushdown zachowuje granicę dowiedzionej semantyki

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D29` (zakres: Junexis Lang).
**Powiązanie zakresów:** Rozwija D27–D28 dla PostgreSQL; izolacja i SQL należą do dostawcy. Rozszerzenie optymalizacji wymaga dowodu zgodności, nie zmiany kompilatora.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M13.8, D27, D28

**Wybrano:** PostgreSQL stosuje te same zasady bezpiecznego pushdown co SQLite:
parametry sterownika, zamknięte identyfikatory, pełny fallback i brak
przybliżania typów. Preflight oraz SQL muszą korzystać z jednego snapshotu
`RepeatableRead`.

**Konsekwencje:** transakcje PostgreSQL providera mają izolację
`RepeatableRead`. Natywny zakres może rosnąć wyłącznie z testem semantycznym i
live proof. Lowerer pozostaje w paczce providera, poza kompilatorem i Hostem.

---

## D30 — Klient targetu materializuje Query Plan, ale nie wybiera providera

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D30` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język materializuje klienta planu odczytu z D24. Binding wybiera wdrożenie; D31 dodaje wywołanie z tasku, D32–D34 zakres modułów i IDE.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M13.9, D21, D24, D25

**Wybrano:** generator `@server` tworzy typowane funkcje Go dla named query z
deklaracji `storage`. Funkcja zawiera wersjonowany model i plan, wiąże argumenty
uruchomieniowe i wywołuje publiczne `QueryPlan` istniejącego klienta Storage
Runtime. Nie zawiera SQL, drivera, adresu Hosta ani wyboru providera.

**Konsekwencje:** kod wygenerowany z jednego `.jnx` może wykonać tę samą
operację przez memory, SQLite, PostgreSQL albo zgodny provider zewnętrzny.
Wersje wire pozostają źródłem zgodności, a binding nadal należy do wdrożenia.
M13.9 nie ustanawia jeszcze składni wywołania query w ciele tasku ani API dla
pozostałych targetów.

---

## D31 — Named query jest typowaną funkcją efektową tasku `@server`

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D31` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język odpowiada za symbol, typ i efekt wywołania query; D30 za wygenerowanego klienta. Fail-fast nie ustanawia ogólnego Result ani odzyskiwania po błędach.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M13.10, D23, D24, D30

**Wybrano:** każda para `storage Model` / `query Operation` tworzy symbol
`Model_Operation(...)` oraz syntetyczny typ `Model_OperationRow`. Wywołanie
zwraca `List<Model_OperationRow>` i jest dozwolone tylko w tasku `@server`.
Kompilator sprawdza parametry, a generator zarządza jednym bindingiem Runtime
dla procesu i obniża funkcję do klienta M13.9.

**Konsekwencje:** developer może przejść od logicznego query do wyniku w kodzie
Junexis bez ręcznego JSON, SQL i Go. Wywołanie jest efektem I/O, dlatego nie
wchodzi do czystego `fn`. Błąd wykonania ma w bootstrapie semantykę fail-fast;
jawny `Result` i strategie recovery wymagają następnej decyzji.

---

## D32 — Import storage udostępnia pochodne API query bez zmiany tożsamości modelu

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D32` (zakres: Junexis Lang).
**Powiązanie zakresów:** D33 rozwija otwartą tu kwestię lokalnego aliasu bez zmiany tożsamości modelu. D34 rozwija widok IDE; nie oznacza to domknięcia re-export.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M13.11, D18, D22, D31

**Wybrano:** import glob lub selective deklaracji `storage`, który zachowuje
jej kanoniczną nazwę, udostępnia w module konsumenta również syntetyczną funkcję
każdego named query i odpowiadający jej typ wiersza. Developer importuje model,
nie ręcznie wygenerowane symbole. Alias modelu nie tworzy alternatywnego API
query, dopóki nie zostanie zdefiniowany jego wpływ na tożsamość wire.

**Konsekwencje:** model danych i task aplikacyjny mogą należeć do osobnych
plików projektu bez duplikowania kontraktów. Semantyka każdego modułu zachowuje
te same kontrole typów i targetu co deklaracja lokalna, a codegen komponuje
osiągalny model tylko przez istniejący graf importów. Aliasowanie storage,
re-export i nawigacja LSP dla symboli syntetycznych pozostają jawnie otwarte.

**Rozwinięcie:** D33 domyka później alias jako nazwę wyłącznie lokalną,
obniżaną do kanonicznej tożsamości przed wspólnym IR. Nie zmienia to przyjętej
tutaj zasady stabilności modelu wire.

---

## D33 — Alias storage jest lokalnym widokiem API, nie tożsamością modelu

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D33` (zakres: Junexis Lang).
**Powiązanie zakresów:** Rozwija D32: alias jest lokalny, kontrakt wire pozostaje kanoniczny. Nie ustanawia aliasów całego modułu ani kwalifikowanych wywołań.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M13.12, D12, D22, D32

**Wybrano:** selective import `StorageName as Alias` syntetyzuje w zakresie
modułu funkcje `Alias_Query` i typy `Alias_QueryRow`. Po walidacji semantycznej
composer obniża je do kanonicznych `StorageName_Query` oraz
`StorageName_QueryRow` przed wspólnym IR. Deklaracja storage nie jest kopiowana
ani przemianowywana.

**Konsekwencje:** alias poprawia czytelność i rozwiązuje lokalne nazewnictwo,
ale nie zmienia modelu, fingerprintu, Query Plan, klienta targetu ani bindingu
providera. Dwa moduły mogą użyć różnych aliasów tego samego storage i nadal
odwołują się do jednego kontraktu danych. Alias całego modułu, re-export oraz
kwalifikowane wywołania query pozostają poza M13.12.

---

## D34 — IDE wyprowadza API query z zakresu modułu, nie z osobnego indeksu nazw

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D34` (zakres: Junexis Lang).
**Powiązanie zakresów:** LSP jest częścią języka i korzysta ze wspólnego bindera D31–D33. Tooling i Studio konsumują ten widok; nie utrzymują niezależnej semantyki nazw query.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M13.13, D18, D31–D33

**Wybrano:** LSP tworzy funkcje `Model_Query` i typy `Model_QueryRow` z
widocznych deklaracji `storage` zwróconych przez projektowy binder symboli.
Lokalny alias zmienia nazwę prezentowaną developerowi, lecz hover i nawigacja
zachowują kanoniczną nazwę modelu oraz lokalizację źródłowej deklaracji query.
Completion nie przeszukuje globalnie wszystkich plików workspace z pominięciem
reguł importu.

**Konsekwencje:** edytor i kompilator pokazują ten sam zakres API, a brak
wcześniejszego otwarcia modułu nie ogranicza completion ani Go to Definition.
Alias całego modułu jest diagnozowany jako `SQC006`, dopóki język nie otrzyma
jawnej składni kwalifikowanego wywołania query. Indeks IDE jest widokiem
pochodnym i nie staje się nowym kontraktem wire ani Runtime.

---

## D35 — Mutacja ma osobny plan i jawny zasięg

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D35` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język określa intencję i zasięg Mutation Plan; D36 składnię, D37 publiczne wywołanie i wynik, D38 wykonanie u dostawcy.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M14.1, D21–D24, D31

**Wybrano:** zapis jest reprezentowany przez wersjonowany
`junexis.storage.mutation-plan/0.1`, oddzielny od read-only Query Plan. Plan
opisuje `Insert`, `Update` lub `Delete`, typowane parametry, przypisania,
predykat oraz jawny zasięg `Single` albo `Many`.

`Update` i `Delete` zawsze wymagają predykatu. Dla `Single` kompilator musi
dowieść równości wszystkich pól klucza z nie-nullable parametrami albo
literałami. `Many` jest świadomą deklaracją zapisu masowego. `Update` nie może
zmieniać klucza, a sam plan nie upoważnia Runtime do automatycznego retry.

**Konsekwencje:** provider nie rozstrzyga intencji ani zasięgu zapisu, a
kompilator nie generuje SQL. Idempotencja, wynik nieznany, wire result,
transakcje wielooperacyjne, składnia źródłowa i wykonanie pozostają osobnymi
decyzjami M14.

---

## D36 — Mutation jest nazwaną operacją modelu storage

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D36` (zakres: Junexis Lang).
**Powiązanie zakresów:** Składnia mutacji i projekcja do planu D35 należą do języka. D43–D44 rozwijają klienta i wywołanie z tasku; historyczny opis etapu nie oznacza ich obecnego braku.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M14.2, D23, D35

**Wybrano:** mutation jest ustrukturyzowanym członkiem deklaracji `storage`:
`mutation Name(params) kind cardinality Entity { set* where? }`. Pole jest
nazywane bez kwalifikatora, a parametr zawsze przez `params.<nazwa>`. Query i
mutation współdzielą jedną przestrzeń nazw operacji modelu.

`single` i `many` są obowiązkową częścią źródła. `set` może przyjąć parametr
albo literał; wyrażenia zależne od bieżącej wartości pola pozostają poza
pierwszym przenośnym kontraktem. Parser i Structural IR zachowują zapis autora,
a osobny projektor uruchamia walidator M14.1 i mapuje problemy na źródło.

**Konsekwencje:** składnia wyraża intencję niezależnie od SQL, providera i
targetu. Poprawna deklaracja M14.2 nie jest jeszcze wywoływalna z tasku ani
wykonywana przez Runtime. Te zdolności wymagają kolejnych pionów.

---

## D37 — Mutacja przekracza Runtime jako write z jawnym wynikiem

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D37` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje publiczne mutation.plan/write i wynik, w tym unknown-outcome. Język obniża plan, Runtime chroni granicę D38, dostawca wykonuje; brak zgody na automatyczne retry.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M14.3, D24, D35–D36

**Wybrano:** Mutation Plan jest obniżany do neutralnej operacji Storage Runtime
`mutation.plan` z efektem `write`, capability `mutation-plan` i wejściem
`junexis.storage.mutation-invocation/0.1`. Query i mutation współdzielą
kanoniczne kodowanie wartości, ale mają osobne schematy wire i osobne reguły
wyniku.

Potwierdzony sukces mutacji używa `junexis.storage.mutation-result/0.1` i
rozróżnia `applied` od `not-applied`, zachowując `affectedRows`. Brak wiedzy,
czy zapis został utrwalony, nie jest trzecim sukcesem: przechodzi kanałem błędu
ABI jako `unknown-outcome`. Automatyczne retry wymaga późniejszego, jawnego
kontraktu idempotencji.

**Konsekwencje:** lowering nie generuje SQL, nie wybiera providera i nie zawiera
sekretów deploymentu. Host i providery muszą osobno zadeklarować oraz
zaimplementować capability `mutation-plan`; fizyczne wykonanie zaczyna się w
M14.4.

---

## D38 — Host chroni capability, a provider wykonuje Mutation Plan atomowo

**Właściciel merytoryczny:** `junexis-runtime`. **Identyfikator źródłowy:** `D38` (zakres: Junexis Lang).
**Powiązanie zakresów:** Runtime odpowiada za efekt i capability na granicy Hosta. Atomowość oraz kernel interpretera należą do dostawców; D39–D42 rozwijają trwałość i optymalizację.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M14.4, D25–D26, D35–D37

**Wybrano:** Host rozpoznaje `mutation.plan` tylko po to, aby wymagać efektu
`write` oraz capability `mutation-plan`. Walidacja i interpretacja Mutation
Plan należą do providera. Referencyjny kernel znajduje się w repo providerów,
poza Runtime SDK, kompilatorem i generatorem targetu.

Provider memory wykonuje plan na kopii snapshotu i publikuje całość dopiero po
walidacji zasięgu, konfliktu klucza i limitów. `single` pasujące do wielu
rekordów nie może pozostawić częściowego skutku. Zero dopasowań jest
potwierdzonym `not-applied`; nierozstrzygnięty skutek nadal wymaga osobnego
błędu `unknown-outcome`.

**Konsekwencje:** inne providery mogą użyć kernela referencyjnego albo własnej
implementacji, lecz nie deklarują `mutation-plan` bez równoważnego testu
zgodności. M14.4 nie przenosi sterownika bazy do Runtime i nie wiąże storage z
backendem `@server`.

---

## D39 — Pierwszy trwały Mutation Plan zachowuje semantykę wspólnego kernela

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D39` (zakres: Junexis Lang).
**Powiązanie zakresów:** Etap trwałego wykonania SQLite poprzedza deltę D40. Wspólny wynik i atomowość pozostają wymaganiem także dla późniejszych optymalizacji.


**Status:** Przyjęta (2026-08-23)
**Kontekst:** M14.5, D19–D20, D26–D27, D38

**Wybrano:** SQLite otrzymuje capability `mutation-plan` dopiero wraz ze
wspólną bramką memory ↔ SQLite i procesowym dowodem trwałości. Pierwsze
wykonanie materializuje ograniczony snapshot przez wspólny kernel, a wynik
zapisuje atomowo w transakcji SQLite. Nie wprowadza jeszcze osobnego lowerera
SQL dla mutacji.

Wywołanie bez transakcji klienta ma własną granicę `Serializable`; wywołanie z
`transactionId` uczestniczy w istniejącym commit/rollback. Brak potwierdzenia
commita pozostaje `unknown-outcome`, a dane planu zawsze trafiają do SQL jako
parametry.

**Konsekwencje:** zgodność jest ustalana przed optymalizacją. Późniejszy
pushdown SQLite lub implementacja PostgreSQL muszą zachować wynik wspólnego
kernela, konflikt klucza, atomowość `single/many` i klasy błędów.

---

## D40 — SQLite optymalizuje wyłącznie dowiedzioną deltę Mutation Plan

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D40` (zakres: Junexis Lang).
**Powiązanie zakresów:** Optymalizacja delty rozwija D39, bez zmiany ABI D37. D42 wydziela wspólny dowód; SQL nadal należy do dostawcy.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M14.6, D27, D38–D39

**Wybrano:** wspólny kernel pozostaje źródłem semantyki mutacji. Provider
SQLite porównuje ograniczony snapshot przed i po jego wykonaniu, dowodzi
zgodności delty z `insert/update/delete`, a następnie zapisuje tylko zmienione
wiersze stałymi, parametryzowanymi poleceniami SQL. Brak dowodu lub delta ponad
limit prowadzi do atomowego fallbacku pełnego snapshotu z M14.5.

**Konsekwencje:** optymalizacja nie zmienia ABI, wyniku ani klas błędów i nie
duplikuje interpretera predykatów. Jest to delta pushdown, nie bezpośrednia
kompilacja predykatu Mutation Plan do SQL. PostgreSQL może przyjąć ten sam
wzorzec dopiero po własnym zgodnym wykonaniu podstawowym.

---

## D41 — PostgreSQL najpierw współdzieli znaczenie mutacji

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D41` (zakres: Junexis Lang).
**Powiązanie zakresów:** Etap bazowy PostgreSQL poprzedza optymalizację D42. Historyczne określenie braku pushdown odnosi się do M14.7, nie wszystkich późniejszych etapów.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M14.7, D28–D29, D38–D40

**Wybrano:** PostgreSQL deklaruje `mutation-plan` dopiero wraz ze wspólną
bramką trzech providerów i live dowodem na izolowanej bazie. Pierwsza
implementacja używa ograniczonego snapshotu `RepeatableRead`, wspólnego
kernela i atomowej publikacji całej encji przez parametryzowany JSONB.

**Konsekwencje:** wymienność providera jest dowiedziona przed optymalizacją.
PostgreSQL nie otrzymuje jeszcze osobnego interpretera predykatów ani mutation
pushdown. Następna optymalizacja musi zachować kernel, wynik, transakcję,
konflikt klucza i przenośne klasy błędów.

---

## D42 — Dowód delty jest wspólny, a SQL pozostaje własnością providera

**Właściciel merytoryczny:** `junexis-storage-providers`. **Identyfikator źródłowy:** `D42` (zakres: Junexis Lang).
**Powiązanie zakresów:** Wspólny dowód delty należy do implementacji dostawców, nie publicznego SDK. Fizyczne polecenia SQL pozostają lokalne; nie wprowadza to nowej algebry języka.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M14.8, D27, D40–D41

**Wybrano:** porównanie wyniku wspólnego kernela ze snapshotem wejściowym i
dowód kształtu `insert/update/delete` należą do provider-neutralnego assembly
Mutation Plan. SQLite i PostgreSQL otrzymują ten wynik przez kontrolowany
`InternalsVisibleTo`, ale samodzielnie mapują go na stały tekst SQL i parametry
własnego sterownika.

**Konsekwencje:** oba trwałe providery nie mogą rozbiec się w znaczeniu delty,
a Runtime ABI nie rośnie o szczegół optymalizacyjny. Brak dowodu pozostaje
sygnałem fallbacku, nigdy zgodą na przybliżone wykonanie. Bezpośrednie
obniżenie predykatu do SQL wymaga osobnej decyzji.

---

## D43 — Generator materializuje klienta mutacji, nie politykę danych

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D43` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język generuje klienta istniejącego ABI D37; D44 rozwija wywołanie z tasku. Dostawca i polityka danych nie przenoszą się do generatora.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M14.9, D21, D35–D38

**Wybrano:** bootstrapowy generator Go tworzy dla każdej named mutation
kwalifikowaną funkcję, typ parametrów i jawny wynik `outcome/affectedRows`.
Funkcja obniża wywołanie do istniejącego `mutation.plan/write`, ale nie wybiera
providera, sterownika, SQL ani danych deploymentu.

**Konsekwencje:** ten sam wygenerowany klient współpracuje z każdym zgodnym
providerem i zachowuje `unknown-outcome` bez automatycznego retry. Integracja
wywołania z ciałem tasku jest osobnym etapem semantyki języka, a nie
odpowiedzialnością klienta Runtime.

---

## D44 — Named mutation jest jawnym efektem tasku server

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D44` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język odpowiada za typowany efekt tasku. Korzysta z klienta D43 i wyniku D37; transakcje wielu operacji i jawne odzyskiwanie wymagają osobnych kontraktów.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M14.10, D31, D35–D38, D43

**Wybrano:** deklaracja mutation tworzy kwalifikowany symbol funkcji oraz
syntetyczny typ potwierdzenia `outcome/affectedRows`. Symbol jest dostępny
wyłącznie w tasku `@server`; backend wiąże go z klientem Runtime i providerem
wybranym przez deployment.

**Konsekwencje:** program Junexis może zapisywać dane bez kodu integracyjnego
Go, lecz nie przejmuje SQL ani wyboru sterownika. W bootstrapie błąd zapisu,
w tym `unknown-outcome`, pozostaje fail-fast i nigdy nie oznacza zgody na retry.
Jawne sterowanie błędem oraz transakcja wielu operacji wymagają osobnych
kontraktów języka.

---

## D45 — Publiczne SDK jest właścicielem kontraktu, nie środowisko wykonawcze

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D45` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK jest źródłem publicznego ABI. D21 definiuje niezależność dostawców; Runtime zachowuje Host, a dostawcy sterowniki i kernel. Nie przenosi to semantyki języka do SDK.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.1–M15.2, D21, M12.10–M12.11

**Wybrano:** `junexis-sdk` jest jednym źródłem publicznych interfejsów ABI,
identyfikatorów i narzędzi autora rozszerzenia. Runtime, dostawcy oraz
zewnętrzne rozszerzenia zależą od wersjonowanej paczki SDK. Runtime zachowuje
Host, cykl życia i transport; repozytorium dostawców zachowuje sterowniki oraz jądra
wykonawcze.

**Konsekwencje:** Runtime nie może ponownie kompilować własnej kopii
Abstractions. Zestaw zgodności może zawierać paczki różnych właścicieli, ale
manifest musi zachować ich pochodzenie. Dla kompatybilności 0.1.0 pierwsza
migracja zachowuje pomocniki Native SQL; ich późniejsze wydzielenie wymaga
jawnej wersji i migracji konsumentów.

---

## D46 — Autor dostawcy zaczyna od działającego rozszerzenia

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D46` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje szablon i generator rozszerzenia. Kompilator ani Runtime nie tworzą konkurencyjnej kopii; D47 dodaje niezależną ocenę zgodności.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.3, D21, D45

**Wybrano:** `junexis-sdk` posiada generator i szablon dostawcy uruchamiany
przez kontrakt `junexis sdk new provider`. Szablon jest neutralną wobec
dostawcy implementacją odczytu, zapisu i kontroli gotowości, zależną wyłącznie
od publicznej paczki Abstractions. Nie deklaruje zdolności, których nie
implementuje.

**Konsekwencje:** kompilator nie kopiuje szablonu, Runtime nie jest zależnością
źródłową wygenerowanego projektu, a główny program uruchamiający jedynie deleguje grupę
poleceń SDK. Generator odrzuca istniejący katalog i nie oferuje destrukcyjnego
wymuszenia nadpisania. Pełny test zgodności pozostaje odpowiedzialnością osobnego M15.4.

---

## D47 — Zgodność dostawcy wymaga niezależnego wykonania

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D47` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK definiuje test zgodności przez publiczne ABI, a dostawca dostarcza implementację. D50 rozwija profil planów; samo zbudowanie szablonu D46 nie jest dowodem zgodności.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.4, D21, D45–D46

**Wybrano:** publiczne SDK dostarcza `junexis sdk test provider`, które buduje i
ładuje rozszerzenie, a następnie sprawdza je wyłącznie przez Storage Runtime
ABI. Autor dostarcza neutralny scenariusz operacji, lecz nie kontroluje logiki
oceny. Raport oraz kody wyjścia odróżniają błąd środowiska od złamania
kontraktu.

**Konsekwencje:** sama kompilacja, manifest albo autorski test uruchomieniowy
nie nadają statusu zgodności. Każda zdolność musi odpowiadać zachowaniu kodu. Profile
Query Plan, Mutation Plan, Native SQL i migracji wymagają osobnych,
wersjonowanych rozszerzeń zestawu testów zgodności.

---

## D48 — Manifest dostawcy jest ścisłym kontraktem

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D48` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje schemat i walidację manifestu źródłowego. D51 rozwija manifest 0.2; metadane wydania i podpis pozostają osobną warstwą D53–D55.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.5, D45–D47

**Wybrano:** SDK publikuje wersjonowane schematy manifestu i scenariusza testowego oraz
polecenie `junexis sdk validate provider`. Walidacja odrzuca obce pola i
porównuje tożsamość, ABI, zdolności, zależność SDK oraz artefakt z
produkcyjnym `.csproj`. Test zgodności zawsze wykonuje tę kontrolę wstępną
przed budowaniem i załadowaniem biblioteki.

**Konsekwencje:** `ProjectReference` dyskwalifikuje zewnętrznego dostawcę, a
rozszerzenie manifestu wymaga nowej wersji schematu. Sumy, podpis, RID i SBOM
pozostają metadanymi późniejszego wydania, nie manifestu źródłowego.

---

## D49 — Zgodność środowiskowa jest rozstrzygana przed wykonaniem

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D49` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK rozstrzyga zgodność ABI przed załadowaniem dostawcy. D59–D60 rozwijają dostępność i zgodność wydań; wynik zgodności nie dowodzi obecności artefaktu dla każdego RID.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.6, D45–D48

**Wybrano:** SDK publikuje maszynowo czytelną macierz ABI i polecenie
`junexis sdk compatibility provider`. Wersja Hosta Runtime oraz zdolności
wymagane przez aplikację są jawnym wejściem. Resolver wykonuje ścisły
kontrolę wstępną i nie ładuje biblioteki dostawcy.

**Konsekwencje:** poprawny manifest nie jest synonimem zgodności z każdym
Hostem. Wynik zawiera konkretną naprawę. RID paczki binarnej pozostaje częścią
przyszłego kontraktu wydania; manifest źródłowy 0.1 zachowuje przenośność.

---

## D50 — Zdolność wykonywania planów wymaga stanowego dowodu

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D50` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje stanowy profil zgodności planów. D52 stosuje go do oficjalnych dostawców; nie zastępuje pomiarów wydajności ani pełnego pokrycia algebry.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.7, M13–M14, D47

**Wybrano:** `query-plan` i `mutation-plan` mają wspólny, jawnie wybierany
profil zestawu testów zgodności. Scenariusz wykonuje uporządkowane kroki w jednej sesji i
porównuje wynik mutacji oraz cały kursor odczytu.

**Konsekwencje:** sama zgodność deskryptora z manifestem nie dowodzi semantyki
planów. Profil wymaga obu zdolności, ale nie staje się obowiązkowy dla
dostawców, którzy ich nie deklarują. Native SQL i migracje pozostają osobnymi
przyszłymi profilami.

---

## D51 — Oficjalny dostawca podlega tej samej granicy SDK

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D51` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje manifest i jednolite wymagania; dostawcy odpowiadają za własne paczki. Manifest 0.2 rozwija D48, bez automatycznego wycofania wsparcia 0.1.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.8, D45–D50

**Wybrano:** manifest 0.2 rozdziela identyfikator Runtime, identyfikator paczki
i typ fabryki oraz jest jednym źródłem metadanych autora i dystrybucji. Oficjalni dostawcy nie
otrzymują wyjątku dla produkcyjnych `ProjectReference`. Konfiguracja testowa
może wskazywać wartość poufną wyłącznie przez dokładne `${env:NAME}`.

**Konsekwencje:** manifest TOML providerów zostaje usunięty, a wspólne kernele
są wersjonowanymi paczkami implementacyjnymi. Manifest 0.1 pozostaje wspierany
dla wygenerowanych dostawców. PostgreSQL nie jest oznaczany jako wykonany na
rzeczywistej bazie, gdy brakuje jawnej wartości poufnej środowiska.

---

## D52 — Oficjalni dostawcy dzielą jeden obserwowalny dowód planów

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D52` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje wspólną ocenę, dostawcy wykonują scenariusz. Brak konfiguracji rzeczywistej bazy oznacza SKIPPED, nie dowód zgodności na bazie.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.9, D50–D51

**Wybrano:** Memory, SQLite i PostgreSQL wykonują ten sam stanowy scenariusz testowy
`query.plan`/`mutation.plan`. Unikalne wartości przebiegu izolują próbę, a
końcowe usunięcie sprząta jej rekord. Brak konfiguracji zewnętrznej bazy daje
status `SKIPPED` (pominięto), nie pozorny sukces na rzeczywistej bazie.

**Konsekwencje:** oficjalny dostawca nie może dowodzić zdolności innym,
słabszym scenariuszem. Test zgodności porównuje publicznie obserwowalne wyniki,
nie implementację, SQL ani użyty sterownik. Wydajność i pełne pokrycie algebry
pozostają osobnymi dowodami.

---

## D53 — Zestaw wydania SDK jest zamkniętym, weryfikowalnym zbiorem

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D53` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK definiuje kompletność własnego zestawu. D54–D57 dodają odtwarzalność i zaufanie; PLATFORM-ADR-003 dotyczy późniejszej publikacji w Registry.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.10, D45–D52

**Wybrano:** manifest wydania wiąże jedną wersję z dokładnym zbiorem `.nupkg`,
ich SHA-256 oraz tożsamością odczytaną z `.nuspec`. Weryfikator nie uruchamia
kodu badanego wydania. Nadmiarowy artefakt jest niezgodnością, nie plikiem
ignorowanym.

**Konsekwencje:** lokalny kandydat może być sprawdzony przed instalacją, a
czysty konsument nie potrzebuje drzewa roboczego repozytorium. Suma bez podpisu nie dowodzi autora;
udostępnienie, podpis, informacja o pochodzeniu i odtwarzalność pozostają otwartymi warunkami
publicznego wydania.

---

## D54 — Odtwarzalność wymaga normalizacji kontenera paczki

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D54` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK odpowiada za normalizację i dowód odtwarzalności swoich paczek. Nie ustanawia to samoistnie odtwarzalności między systemami ani gotowości oficjalnego wydania.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.11, D53

**Wybrano:** dowodem odtwarzalności są identyczne bajty dwóch niezależnych
procesów budowania, nie tylko ustawienie kompilatora. Po `dotnet pack` normalizowane są
losowe metadane i czasy ZIP. Osobny opis pochodzenia wiąże wynik z rewizją,
stanem źródła, konfiguracją i zestawem narzędzi.

**Konsekwencje:** suma wydania jest liczona dopiero po normalizacji. Drzewo
`dirty` może służyć do lokalnego dowodu, lecz nie otrzymuje `sourceReady`.
Dowód na jednej platformie nie jest automatycznie dowodem międzyplatformowym,
a niepodpisana informacja o pochodzeniu nie gwarantuje autentyczności.

---

## D55 — Podpis jest oddzielną warstwą nad odtwarzalnym wydaniem

**Właściciel merytoryczny:** `junexis-sdk`. **Identyfikator źródłowy:** `D55` (zakres: Junexis Lang).
**Powiązanie zakresów:** SDK utrzymuje warstwę podpisu zestawu SDK. D56–D57 rozwijają zaufanie; PLATFORM-ADR-004 określa wspólną politykę i role, Authority chroni klucze. Kontrakt podpisu nie oznacza uruchomionej oficjalnej publikacji.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.12, D53–D54

**Wybrano:** zestaw wydania jest podpisywany po odtwarzalnym procesie
budowania. Dokument `junexis.sdk.signature/0.1` wiąże sumy manifestu i
informacji o pochodzeniu, wersję oraz jawny identyfikator klucza. Weryfikacja
wymaga wskazania zaufanego klucza publicznego; klucz prywatny nigdy nie należy
do zestawu wydania.

**Konsekwencje:** podpis potwierdza autentyczność konkretnych bajtów, lecz nie
zastępuje kontroli kompletności ani zgodności. Zmienione artefakty, obcy klucz
i inny identyfikator są odrzucane. Oficjalny klucz, jego ochrona, rotacja,
unieważnianie i publiczne źródło zaufania pozostają osobnym etapem
dystrybucji.

---

## D56 — Zaufanie do wydania pochodzi spoza badanego zestawu

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D56` (zakres: Junexis Lang).
**Powiązanie zakresów:** Platforma odpowiada za zasadę zewnętrznego źródła zaufania; SDK za wskazany kontrakt magazynu. PLATFORM-ADR-004 rozwija operacyjny podział ról, nie upoważnia badanego zestawu do samodzielnego ustanowienia zaufania.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.13, D55

**Wybrano:** zewnętrzny magazyn `junexis.sdk.trust-store/0.1` rozstrzyga, które
klucze mogą podpisywać wydania SDK. Klucz ma jawne przeznaczenie, okres
ważności i status. Rosnąca rewizja oraz minimalna rewizja znana odbiorcy
chronią przed podaniem polityki sprzed unieważnienia.

**Konsekwencje:** badany zestaw nie może dostarczyć własnego magazynu zaufania.
Rotacja może nakładać okresy dwóch aktywnych kluczy, ale klucz unieważniony
jest odrzucany bez względu na badany czas. Kontrakt nie ustanawia sam z siebie
oficjalnego klucza ani bezpiecznego kanału dystrybucji magazynu.

---

## D57 — Klucz główny podpisuje politykę, nie każde wydanie

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D57` (zakres: Junexis Lang).
**Powiązanie zakresów:** Platforma utrzymuje rozdział klucza głównego i kluczy ról, rozwinięty w PLATFORM-ADR-004; Authority odpowiada za ochronę i użycie kluczy. Lokalny dowód SDK nie jest dowodem przeprowadzenia oficjalnej ceremonii.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.14, D55–D56

**Wybrano:** oddzielny klucz główny P-256 podpisuje dokładne bajty i rewizję
magazynu zaufania. Klucze wydawnicze podpisują zestawy SDK i mogą być obracane
przez publikację nowej, podpisanej polityki. Pełna weryfikacja zachowuje odcisk
klucza głównego.

**Konsekwencje:** prywatny klucz główny może pozostawać poza zwykłym procesem
wydawniczym i katalogiem publikacji. Odbiorca nadal musi otrzymać publiczny
klucz główny lub jego odcisk z niezależnego, zaufanego kanału; podpis nie
rozwiązuje sam problemu pierwszego ustanowienia zaufania.

---

## D58 — Zamknięcie SDK nie jest ogłoszeniem publicznej dystrybucji

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D58` (zakres: Junexis Lang).
**Powiązanie zakresów:** Platforma rozdziela zamknięcie SDK od publicznej dystrybucji. PLATFORM-ADR-003–005 rozwijają publikację, zaufanie i instalację; bieżące wstrzymanie pełnego zamknięcia M19/M20 nie cofa decyzji SDK.


**Status:** Przyjęta (2026-08-24)
**Kontekst:** M15.15, D45–D57

**Wybrano:** M15 zamyka publiczne kontrakty SDK, narzędzia autora, zgodność
dostawców oraz lokalny łańcuch zaufania. Status wymaga zbiorczego przejścia
bramek M15.1–M15.14 i kontroli kompletu schematów.

**Konsekwencje:** oficjalne klucze, publiczne źródło paczek, instalator,
aktualizacje i operacyjna procedura publikacji pozostają osobnym kamieniem
milowym. Ich brak nie może być ukrywany pod statusem SDK, ale nie pozostawia
też M15 otwartego bez końca.

---

## D59 — Manifest wydania opisuje dostępność, nie zamiar

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D59` (zakres: Junexis Lang).
**Powiązanie zakresów:** Platforma definiuje prawdziwy stan dostępności, SDK publiczny schemat, Instalator i portal konsumują wynik. D60 dodaje rodzinę zgodności; nie zamienia deferred w available.


**Status:** Przyjęta (2026-08-25)
**Kontekst:** M16.1, D58

**Wybrano:** jedno wydanie platformy dla konkretnego systemu i architektury
jest opisane manifestem `junexis.platform-release/0.1`. Każdy komponent ma
dokładnie jeden stan: `available` z własnym artefaktem, `embedded` ze wskazaniem
artefaktu nadrzędnego albo `deferred` z jawnym uzasadnieniem. Pole `required`
opisuje docelową rolę w platformie i nie zastępuje stanu dostępności.

**Konsekwencje:** lokalny kandydat może uczciwie wymieniać Runtime, SDK lub
dostawcę magazynu jako wymagany, lecz jeszcze odroczony. Portal i instalator nie
mogą wyprowadzić przycisku pobierania z samej obecności komponentu w manifeście.
Plik dostępny musi mieć zweryfikowany rozmiar i SHA-256, a niezadeklarowany plik
w katalogu wydania powoduje odrzucenie całego zestawu.

---

## D60 — Zestaw zgodności jest rodziną wydań, nie kopią numeru wersji

**Właściciel merytoryczny:** `junexis-platform`. **Identyfikator źródłowy:** `D60` (zakres: Junexis Lang).
**Powiązanie zakresów:** Platforma utrzymuje rodzinę zgodności i role komponentów; SDK utrzymuje publiczne kontrakty. D59 nadal rozstrzyga dostępność, a PLATFORM-ADR-005 cykl życia instalowanego zestawu.


**Status:** Przyjęta (2026-08-25)
**Kontekst:** M16.2, D59

**Wybrano:** `compatibilitySet` wskazuje wersjonowany katalog
`junexis.platform-compatibility/0.1`. Katalog definiuje przedział SemVer wydania
i komponentów, kanał, środowiska docelowe, kompletny inwentarz, role, wymagalność,
dopuszczalne stany, kontrakty i dokładny graf zależności. Pierwsza rodzina ma
identyfikator `junexis-0.1-preview`.

**Konsekwencje:** wiele kolejnych wydań może należeć do jednej rodziny bez
tworzenia katalogu dla każdego numeru. Zmiana roli, kontraktu lub grafu wymaga
świadomej zmiany katalogu. Zgodność nie zastępuje dostępności: komponent
odroczony może należeć do zgodnego zestawu, lecz nie staje się przez to gotowym
artefaktem.

---

## D61 — Dostępny LSP musi dowieść tożsamości przez uruchomienie

**Właściciel merytoryczny:** `junexis-lang`. **Identyfikator źródłowy:** `D61` (zakres: Junexis Lang).
**Powiązanie zakresów:** Język jest producentem LSP i odpowiada za jego tożsamość. SDK i narzędzia wydania walidują kontrakt, Instalator wykonuje kontrolę przed aktywacją. Opis Windows dotyczy pionu M16.3, nie dowodu uruchomienia wszystkich systemów M19.


**Status:** Przyjęta (2026-08-25)
**Kontekst:** M16.3, D59–D60

**Wybrano:** paczka LSP dla Windows x64 jest samodzielną dystrybucją ReadyToRun
z wewnętrznym manifestem `junexis-lsp.json`. Proces publikacji nadaje tę samą
wersję binarium, manifestowi komponentu i manifestowi całej platformy.
Walidator uruchamia punkt wejścia z `--version` i porównuje rzeczywistą
tożsamość z deklaracją.

**Konsekwencje:** istnienie EXE albo poprawna suma ZIP nie wystarcza do oznaczenia
LSP jako `available` i `verified`. Dystrybucja wykrywa inną wersję, obcą
tożsamość, brak samodzielności, niebezpieczną ścieżkę i brak punktu wejścia.
Instalowanie i automatyczne podłączanie klientów pozostaje oddzielnym etapem.

---

## D62 — Instalacja LSP ma jedno położenie i zachowuje poprzednią wersję

**Właściciel merytoryczny:** `junexis-installer`. **Identyfikator źródłowy:** `D62` (zakres: Junexis Lang).
**Powiązanie zakresów:** Instalator odpowiada za bezpieczną instalację, Studio i Tooling za odkrywanie LSP. Opis stałego katalogu dotyczy samodzielnego LSP z M16.4; PLATFORM-ADR-005 określa odrębny, nowszy układ całego wydania M20 z katalogami wersji i wskazaniem aktywnego zestawu. Nie należy przenosić podmiany katalogu M16 na aktywne wydanie M20.


**Status:** Przyjęta (2026-08-25)
**Kontekst:** M16.4, D61

**Wybrano:** Instalator traktuje samodzielny ZIP LSP jako aplikację platformy i
instaluje go na Windows w `%LOCALAPPDATA%\Junexis\lsp`. Najpierw rozpakowuje do
katalogu roboczego, sprawdza manifest, uruchamia EXE z `--version`, a dopiero potem atomowo
podmienia aktywny katalog. Studio i rozszerzenie VS Code znają ten sam kontrakt
położenia.

**Konsekwencje:** użytkownik nie musi ustawiać ścieżki ani budować repozytorium.
Błędna paczka nie niszczy działającej instalacji. Jawna konfiguracja nadal ma
pierwszeństwo, a odpowiedniki ścieżki dla macOS i Linux są ustalone przed
powstaniem artefaktów dla tych systemów.

---

## Historia zmian specyfikacji

| Wersja | Data | Zmiana |
|--------|------|--------|
| 0.1 | 2025-10 | Pierwsze wydanie języka — basic tasks, chan, queue, stream |
| 0.20 | 2025-11 | pipeline, narrative, supervisor, fn, circuit_breaker, quirks |
| 0.31-0.37 | 2025-11-26 | Pełne typowanie IR, code gen dla all platform |
| 0.38-0.46 | 2026-01 | state keyword, API declaration, REST/gRPC/WebSocket generators |
| 0.49 | 2026-04 | MQTT, CoAP, AMQP protocols, OpenAPI generation, spec stabilization |
| 1.0.0 (wycofane oznaczenie) | 2026-04-06 | Specyfikację ogłoszono wówczas jako stabilną; ocena została wycofana po audycie implementacji. |
| **0.2.0 Experimental** | **2026-06-23** | **Rebaseline dojrzałości: konsolidacja specyfikacji, gramatyki, AST, semantyki, IR, codegenu, runtime i E2E.** |
| **D16** | **2026-06-23** | **Przyjęto hybrydowy model wykonania i rolę C++/Go/TypeScript jako backendów bootstrapowych.** |
| **D17** | **2026-06-23** | **Przyjęto `resource` jako eksperymentalną odpowiedzialność stanu/I/O oraz rozdzielono komunikacyjny stream od sekwencyjnego I/O.** |
| **D18** | **2026-07-02** | **Przyjęto project-first workflow: manifest projektu jest kierunkiem docelowym, a tryb pojedynczego pliku pozostaje compatibility/playground/regression corpus.** |
| **D19** | **2026-08-14** | **Wybrano referencyjny provider SQLite oparty na przypiętym driverze pure-Go i obowiązkowym teście z `CGO_ENABLED=0`.** |
| **D20** | **2026-08-14** | **Przyjęto wspólny interfejs `storage.Store` i niezależny provider memory z transakcją copy-on-write.** |
| **D21** | **2026-08-21** | **Oddzielono deklarację storage i provider runtime od targetu, backendu oraz protokołu aplikacji.** |
| **D22** | **2026-08-22** | **Przyjęto logiczny model danych i przenośny Query Plan IR przed loweringiem do providera.** |
| **D23** | **2026-08-22** | **Przyjęto deklarację `storage`, jawne parametry `params.*` i projekcję źródła do kontraktów M13.1.** |
| **D24** | **2026-08-22** | **Przyjęto `query.plan` jako neutralną operację istniejącego Storage Runtime ABI oraz wersjonowane wejście M13.3.** |
| **D25** | **2026-08-22** | **Host wymusza capability `query-plan`, a provider odpowiada za interpretację algebry i fizyczne wykonanie.** |
| **D26** | **2026-08-22** | **Referencyjne providery współdzielą semantykę Query Plan, zachowując osobny scan, transakcje i budżety fizyczne.** |
| **D27** | **2026-08-22** | **Pushdown providera obejmuje tylko dowiedziony podzbiór semantyki; pozostałe plany używają zgodnego fallbacku.** |
| **D28** | **2026-08-22** | **PostgreSQL najpierw współdzieli semantykę Query Plan na ograniczonym snapshotcie; natywny pushdown jest osobnym etapem.** |
| **D29** | **2026-08-23** | **PostgreSQL pushdown obejmuje tylko dowiedzioną semantykę i wiąże preflight z SQL snapshotem `RepeatableRead`.** |
| **D30** | **2026-08-23** | **Generator targetu materializuje typowany klient Query Plan bez wyboru providera, SQL i sekretów deploymentu.** |
| **D31** | **2026-08-23** | **Named query tworzy typowaną funkcję efektową i syntetyczny typ wiersza dostępne wyłącznie w tasku `@server`.** |
| **D32** | **2026-08-23** | **Import modelu storage udostępnia pochodne API query bez zmiany jego kanonicznej tożsamości.** |
| **D33** | **2026-08-23** | **Alias storage jest lokalnym widokiem API obniżanym do kanonicznej tożsamości przed wspólnym IR.** |
| **D34** | **2026-08-23** | **IDE wyprowadza API named query z projektowego zakresu modułu i zachowuje kanoniczną tożsamość Runtime.** |
| **D35** | **2026-08-23** | **Mutacja ma osobny provider-neutralny plan, jawny zasięg Single/Many i brak domyślnego retry.** |
| **D36** | **2026-08-23** | **Mutation jest nazwaną operacją storage z obowiązkowym zasięgiem i projekcją do neutralnego planu.** |
| **D37** | **2026-08-23** | **Mutation Plan przechodzi przez Storage Runtime jako `mutation.plan`/`write`, a wynik nieznany pozostaje błędem `unknown-outcome`.** |
| **D38** | **2026-08-23** | **Host chroni capability `mutation-plan`, a provider wykonuje plan atomowo i potwierdza skutek jawnym wynikiem.** |
| **D39** | **2026-08-23** | **SQLite wykonuje Mutation Plan przez wspólny kernel i atomową transakcję, z dowodem zgodności oraz trwałości po restarcie.** |
| **D40** | **2026-08-24** | **SQLite zapisuje wyłącznie dowiedzioną deltę Mutation Plan parametryzowanym SQL i zachowuje atomowy fallback.** |
| **D41** | **2026-08-24** | **PostgreSQL wykonuje wspólny Mutation Plan atomowo i przechodzi live conformance trzech providerów przed własną optymalizacją.** |
| **D42** | **2026-08-24** | **SQLite i PostgreSQL współdzielą dowód delty Mutation Plan, lecz każdy provider zachowuje własny parametryzowany SQL.** |
| **D43** | **2026-08-24** | **Generator Go materializuje typowane API named mutation bez wyboru providera, SQL i polityki retry.** |
| **D44** | **2026-08-24** | **Named mutation jest typowanym efektem tasku server z jawnym potwierdzeniem i bez automatycznego retry.** |
| **D45** | **2026-08-24** | **Publiczne kontrakty rozszerzeń mają jedno źródło w `junexis-sdk`; Runtime i dostawcy korzystają z wersjonowanej paczki.** |
| **D46** | **2026-08-24** | **SDK prowadzi autora od działającego dostawcy i nie nadpisuje istniejącego kodu.** |
| **D47** | **2026-08-24** | **Zgodność dostawcy wymaga niezależnego wykonania przez publiczny zestaw testów zgodności.** |
| **D48** | **2026-08-24** | **Manifest dostawcy jest ścisłym, wersjonowanym kontraktem zgodnym ze scenariuszem testowym i projektem.** |
| **D49** | **2026-08-24** | **Zgodność ABI, SDK, Runtime i zdolności jest jawnie rozstrzygana przed wykonaniem dostawcy.** |
| **D50** | **2026-08-24** | **Query Plan i Mutation Plan wymagają stanowego profilu zgodności, nie samej deklaracji zdolności.** |
| **D51** | **2026-08-24** | **Oficjalni dostawcy podlegają tej samej granicy paczek, manifestu i bezpiecznej konfiguracji co rozszerzenia zewnętrzne.** |
| **D52** | **2026-08-24** | **Memory, SQLite i PostgreSQL dzielą jeden stanowy, powtarzalny dowód Query Plan i Mutation Plan.** |
| **D53** | **2026-08-24** | **Kandydat wydania SDK jest zamkniętym zbiorem paczek weryfikowanym bez uruchamiania jego kodu.** |
| **D54** | **2026-08-24** | **Odtwarzalność SDK jest dowodzona identycznymi bajtami po normalizacji kontenera NuGet.** |
