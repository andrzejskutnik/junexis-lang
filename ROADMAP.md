# Roadmap — junexis-lang

> **Status projektu:** Junexis Lang 0.2.0 Experimental. Specyfikacja i implementacja są konsolidowane; żadna warstwa nie otrzymuje domyślnie statusu stabilnego.  
> **Ostatnia aktualizacja:** 2026-08-13

---

## Legenda

| Symbol | Znaczenie |
|--------|-----------|
| ✅ | Gotowe |
| 🔨 | W trakcie |
| ⏳ | Zaplanowane |
| 🔮 | Planowane długoterminowo |
| ❌ | Poza aktualnym zakresem 0.2 |

---

## Milestone 1 — Fundament kompilatora (M1) ✅

**Cel:** Parser + AST. Plik `.jnx` wchodzi, drzewo AST wychodzi.

**Kontrakt domknięcia:** M1 obejmuje wyłącznie lexer, parser i budowę AST. Nie wymaga
poprawności semantycznej, IR ani codegen. Błąd wejścia lub budowy AST jest zwracany jako
diagnostyka, a nie nieobsłużony wyjątek.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `ASTBuilder` — AST dla całej składni osiągalnej z `program` | `Core` | ✅ |
| Publiczne `JunexisCompiler.ParseAsync()` / `ParseSource()` → `ParseResult` | `Core` | ✅ |
| CLI: `junexis parse <plik.jnx>` (bez semantyki) | `CLI` | ✅ |
| 13 głównych przykładów buduje AST — test całego korpusu | `Tests` | ✅ |
| 14 przykładów `examples/grammar` buduje AST — test całego korpusu | `Tests` | ✅ |
| Diagnostyki parsera `P0001` i błędu budowy AST `P0002` | `Core` | ✅ |
| Lokalizacje AST i diagnostyk są 1-based — test kontraktu | `Tests` | ✅ |
| Globalne polecenie narzędzia ma nazwę `junexis` | `CLI` | ✅ |

**Weryfikacja M1 (2026-06-23):** 27/27 plików obu korpusów buduje `ProgramNode`.
Usunięto nieosiągalną regułę `rangeExpression`; `ASTBuilder` nie pomija już po cichu
deklaracji bez reprezentacji AST. Poprawność dalszych etapów jest śledzona przez kolejne
milestone'y i nie wpływa na status parsera.

---

## Milestone 2 — IR ✅

**Cel:** AST → bezstratny Structural IR. Każda konstrukcja osiągalna z parsera
zostaje zachowana albo transformacja kończy się kontrolowaną diagnostyką.

M2 nie obejmuje jeszcze rozwiązywania symboli, wnioskowania typów, wyboru
protokołów, placementu ani platform lowering. Typed Semantic IR i Distributed
Topology IR są osobnymi etapami kierunku D16.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `IRDocument`, `IRTask`, `IRCommType`, `IRPipeline` — typy Structural IR | `IR` | ✅ |
| `AstToIrTransformer` — transformacja AST → IR | `IR` | ✅ |
| `IRTask.Native` — propagacja `native {}` z task body | `IR` | ✅ |
| Inicjalizatory jako `IRExpr`; jednostki czasu i pamięci jako `IRUnitValue` | `IR` | ✅ |
| `IRTransformResult` + kontrolowane diagnostyki `R0001`/`R0002` | `IR` | ✅ |
| Origin map: ścieżka pochodzenia Structural IR → plik/wiersz/kolumna | `IR` | ✅ |
| Wyczerpująca transformacja bez cichego pomijania węzłów | `IR` | ✅ |
| Testy AST → IR dla 13 przykładów głównych i 14 przykładów gramatyki | `Tests` | ✅ |

**Weryfikacja M2 (2026-06-23):** 27/27 plików buduje Structural IR. Testy
kontraktu obejmują wyrażenia inicjalizujące, jednostki, `native`, source map i
diagnostyki transformacji. Deskryptory tras bridge pozostają tymczasowym polem
kompatybilności istniejącego codegenu; ich przeniesienie do Topology IR / platform
lowering należy do D16, a nie do kontraktu M2.

### Walidacja Core używana przed IR 🔨

| Zadanie | Projekt | Status |
|---------|---------|--------|
| Platform validator | `Core` | 🔨 |
| Profile YAML + inheritance | `Core` | ✅ |
| Nieznana/eksperymentalna platforma → `W5000`, bez awarii procesu | `Core` | ✅ |
| Diagnostyki semantyczne `E0001`–`E0099` | `Core` | 🔨 |

---

## Milestone 3 — Analiza topologii pipeline ✅

**Cel:** Structural IR → logiczny, port-aware graf tasków i relacji. M3 opisuje
znaczenie połączeń przed wyborem transportu, protokołu, deploymentu i platform
lowering.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `PipelineGraph` — zadeklarowane taski oraz port-aware krawędzie z IR | `Topology` | ✅ |
| Dangling task → `T0002`, bez tworzenia pozornego węzła | `Topology` | ✅ |
| Tarjan SCC + rzeczywista ścieżka cyklu | `Topology` | ✅ |
| Fan-in/fan-out liczone po unikalnych logicznych endpointach | `Topology` | ✅ |
| Głębokość tasków i `MaxDepth` na DAG-u kondensacji SCC | `Topology` | ✅ |
| Diagnostyki `T0001`–`T0005` z origin location | `Topology` | ✅ |
| Integracja z CLI `check`, `compile`, `deploy`, `validate-example` | `CLI` | ✅ |
| Testy `.jnx → AST → IR → topology` dla 13 głównych przykładów | `Tests` | ✅ |

**Weryfikacja M3 (2026-06-23):** wszystkie 13 głównych przykładów buduje
logiczną topologię bez diagnostyk Error. Cykl pozostaje ostrzeżeniem, ponieważ
może być świadomym sprzężeniem systemu rozproszonego. Wybór protokołu, analiza
granic platform i placement zostały następnie podjęte przez baseline
Distributed Topology IR w M4d (D16).

---

## Milestone 4 — Code Generation ✅ (baseline 0.2)

**Cel:** IR → kod docelowy dla trzech platform.

Wspólne kryteria akceptacji M4 opisuje `spec/11-code-generation.md`. Rozróżniamy
L0 (generator nie upada), L1 (kompletny artefakt), L2 (natywny build), L3
(zgodność zachowania) i L4 (zgodność rozproszona).

| Wspólna bramka | Status |
|---|---|
| `GeneratedArtifactContract` — bezpieczny i kompletny inventory | ✅ |
| L1 dla wszystkich 13 głównych przykładów | ✅ |
| CLI `artifact-gate` dla PlatformIO, Go i npm/TypeScript | ✅ |
| L2 embedded: PlatformIO build dla 10/10 właściwych przykładów | ✅ |
| L2 server: `go build` dla 12/12 właściwych przykładów | ✅ |
| L2 report: `tsc --noEmit` dla 4/4 właściwych przykładów | ✅ |
| Wąski test L3: wykonywalny task Go + wstrzyknięty `extern` | ✅ |
| L4 server → report: uruchomiony Go + skompilowany TypeScript + versioned stream envelope | ✅ |
| Pełne L3 wszystkich backendów i macierz L4 wszystkich transportów | ⏳ rozszerzenie |

### 4a. @embedded → C++/PlatformIO

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `CppCodeGenerator` — task → projekt PlatformIO | `CodeGen` | 🔨 |
| `device` → konkretny profil płytki i biblioteki | `CodeGen` | 🔨 |
| `chan<T>` → FreeRTOS queue | `CodeGen` | ⏳ |
| `native { @c/c++ { } }` → include/library/setup | `CodeGen` | 🔨 |
| `every <duration>` → `vTaskDelay` | `CodeGen` | ✅ |
| WiFi/FS/FreeRTOS bootstrap w `main.cpp` | `CodeGen` | 🔨 |
| Canonical regression tests (5 testów) | `Tests` | ✅ |
| Natywny build głównego korpusu (10 artefaktów) | `artifact-gate` | ✅ L2 |

### 4b. @server → Go

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `GoCodeGenerator` — task → Go service | `CodeGen` | ✅ |
| Samodzielny moduł (`main.go` + `go.mod`) | `CodeGen` | ✅ |
| `chan<T>` / `queue<T>` / `stream<T>` | `CodeGen` | ✅ |
| `send` stmt → `result = value` (scalar output) | `CodeGen` | ✅ |
| `protocol rest { }` → typowany `net/http` routing + OpenAPI | `CodeGen` | ✅ |
| `protocol grpc { }` → `.proto` + stubs | `CodeGen` | ⏳ |
| Natywny build głównego korpusu (12 artefaktów) | `artifact-gate` | ✅ L2 |
| Wstrzykiwalne, typowane granice `extern` + wykonywalny test | `CodeGen/Tests` | ✅ L3 (wąski) |

### 4c. @report → TypeScript

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `TypeScriptCodeGenerator` — samodzielny projekt TS | `CodeGen` | 🔨 |
| lokalny runtime + typy wbudowane + poprawne importy | `CodeGen` | 🔨 |
| `stream<T>` / `queue<T>` / `chan<T>` | `CodeGen` | 🔨 |
| `send` stmt → `result = value` (scalar output) | `CodeGen` | ✅ |
| Natywny build głównego korpusu (4 artefakty) | `artifact-gate` | ✅ L2 |

### 4d. Adaptery protokołów (automatyczne)

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `@embedded → @server`: obecny TCP/JSON bootstrap, docelowo protocol-driven | `CodeGen` | 🔨 |
| `@server → @report`: HTTP/WebSocket bootstrap | `CodeGen` | 🔨 |
| `protocol` → semantic bindings w Distributed Topology IR | `Topology` | ✅ (REST executable) |
| `deployment { }` → placements, replicas, `deployment-plan.json` i compose | `Topology/CLI` | ✅ |
| Wersjonowany wire contract `junexis-wire/0.2` | `CodeGen` | ✅ (HTTP JSON) |
| Rzeczywisty test Go → TypeScript | `Tests` | ✅ L4 |

---

## Milestone 5 — CLI i LSP ✅ (baseline 0.2)

Kontrakt i poziomy dowodu C0–C3 opisuje `spec/12-cli-and-lsp.md`. Baseline
obejmuje rzeczywisty proces CLI, sesję LSP JSON-RPC przez stdio i pakowalny
artefakt rozszerzenia; nie obejmuje jeszcze workspace-wide symbol index ani
pełnego zestawu refactorings IDE.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| `junexis build <plik.jnx> -o <out-dir>` | `CLI` | ✅ |
| `junexis check <plik.jnx>` | `CLI` | ✅ |
| LSP `textDocument/didOpen` + diagnostyki | `LspHost` | ✅ |
| LSP `textDocument/didChange` + `didClose` | `LspHost` | ✅ |
| LSP `textDocument/hover` — typy, pola, warianty | `LspHost` | ✅ |
| LSP `textDocument/completion` — keywords, typy, platformy | `LspHost` | ✅ |
| LSP `textDocument/definition` | `LspHost` | ✅ |
| LSP `textDocument/documentSymbol` | `LspHost` | ✅ |
| LSP `textDocument/semanticTokens` — highlighting | `LspHost` | ✅ |
| VS Code extension (repo: `junexis-tooling`) | zewnętrzne | ✅ C3 |

---

## Milestone 5.5 — Compiler/runtime conformance ✅

Obowiązkowa bramka przed M6. Kryteria R0–R4 i macierz semantyczna znajdują się
w `spec/13-compiler-runtime-baseline.md`.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| Deterministyczna bramka całego kompilatora i LSP | `junexis-lang` | ✅ |
| Deterministyczna bramka runtime .NET | `junexis-runtime` | ✅ 540/540 ×2 |
| Build + wykonywalne conformance embedded C | `junexis-runtime` | ✅ CTest 2/2 |
| Build + conformance embedded Rust | `junexis-runtime` | ◻ eksperymentalne, poza bramką; brak Cargo |
| Macierz `chan` / `queue` / `stream` aktywnych runtime'ów | oba repo | ✅ |
| Compiler → runtime contract dla trzech targetów | oba repo | ✅ C++ / Go / TypeScript |
| Reprezentatywny pionowy system R4 | oba repo | ✅ Go → TypeScript, `junexis-wire/0.2` |

---

## Milestone 6 — Narrative i Embedded Toolchain

| Zadanie | Projekt | Status |
|---------|---------|--------|
| M6.1 canonical trace contract — polityka, event model, manifest | `CodeGen` / `Narrative` | ✅ |
| `NarrativeGenerationStage` — blok `narrative` → metadane trace | `Narrative` | 🟡 polityka + source identity |
| Probe injection — trace points w generated code | `Narrative` | ✅ C++ + Go + TypeScript |
| Pionowy artefakt Narrative trace | `CodeGen.Tests` | ✅ C++ (`g++`) + TypeScript (`tsc` + Node) |
| M6.2 embedded trace transport | `CodeGen` / `Embedded.Toolchain` | ✅ sink runtime + aktywny HTTP ESP32 + build PlatformIO |
| M6.3 przepływ przez rzeczywisty TraceCollector | platforma | ✅ C++ → Go → TypeScript, Why depth 4, Story 1.0 |
| M6.4 Diagnostic Story + Mentor w Studio | `Narrative` / `Mentor` / `Studio` | ✅ Story 1.1, runtime build identity, evidence-bounded Mentor join |
| M6.5 zarządzana sesja Narrative w Studio | `Narrative` / `Studio` | ✅ latest-build → loopback Collector → health → latest build event |
| M6.6 kontekst Narrative dla Run/Debug | `Studio` / `CodeGen` | ✅ atomowe URL + build ID dla C++/Go/TypeScript Run i DAP launch |
| M6.7 Narrative-aware launch coordinator | `Studio` / `Narrative` | ✅ bounded wait Starting → Ready, cancel, timeout fail-open, attach bez oczekiwania |
| M6.8 aktywna pętla runtime → Studio | `Studio` / `Narrative` | ✅ activity state + delta + auto-follow latest Diagnostic Story |
| M6.9 debugger → filtrowana Diagnostic Story | `Studio` / `Narrative` | ✅ stopped auto-load, build/Narrative/kind filters, pause/resume follow |
| M6.10 historyczna nawigacja Diagnostic Story | `Studio` / `Narrative` | ✅ filtered previous/next, pin, evidence comparison, debug context retention |
| M6.11 trwałe sesje diagnostyczne | `Studio` / `Narrative` | ✅ named workspace bookmarks, offline reopen, checksummed evidence bundle |
| M6.12 wymiana sesji diagnostycznych | `Studio` / `Narrative` | ✅ bounded import, SHA-256 verification, relational validation, read-only offline replay |
| M6.13 inspekcja sesji diagnostycznych | `Studio` / `Narrative` | ✅ bundle provenance, verified evidence inventory, directional imported–live comparison |
| M6.14 autentyczność sesji diagnostycznych | `Studio` / `Narrative` | ✅ optional ECDSA P-256 manifest signature, fingerprint trust, downgrade-visible status |
| M6.15 recenzja spraw diagnostycznych | `Studio` / `Narrative` / `Mentor` | ✅ immutable evidence + separate semantic review, revision history, optional signature, portable diagnostic case |
| M6.16 propozycje Mentora w recenzji | `Studio` / `Narrative` / `Mentor` | ✅ offline exact-build proposals, no Mentor Decision, explicit accept/dismiss, durable source provenance |
| `EmbeddedToolchain.Flash()` — deploy na ESP32 | `Embedded.Toolchain` | ⏳ |
| PlatformIO integration — `platformio.ini` + build | `Embedded.Toolchain` | ✅ baseline ESP32-S3 |

---

## Milestone 7 — System Delivery

**Cel:** project → build → package → deploy → observe jako jeden jawny przepływ
systemu. M7 pozostaje eksperymentalne i nie ustanawia stabilności platformy.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| M7.1 Experimental System Bundle 0.1 | `CLI` / `CodeGen` / platforma | ✅ deterministic project latest-build ZIP, exact SHA-256 inventory, offline bounded verification |
| M7.2 `junexis package` z build/gates preflight | `CLI` / toolchain | ✅ analysis Build ID → native gates → bound build → create + offline verify |
| M7.3 deployment preflight + dyskretna opinia Mentora | platforma | ✅ package receipt + technical readiness; exact-Build Mentor review zawsze non-blocking |
| M7.4 lokalny deployment Go + TypeScript | `CLI` / runtime | ✅ verified bundle/receipt/preflight → selective re-hash extraction → Go + TypeScript bounded foreground session on loopback → owned-process cleanup |
| M7.5 Deploy Center w Studio | `Studio` | ✅ verified latest-build receipt → readiness → package/preflight → bounded local deploy for `server`/`report`; package/preflight identity is pinned to the selected verified Build ID, failed receipts reach audit, focused host/extension coverage is green, and full `JunexisStudio.sln` remains green |
| M7.6 lifecycle i deployment receipt | platforma | ✅ deterministic `deployment-receipt.json` plus `deployment-session.json` 0.2; owner lease/heartbeat, long-lived host PID, per-target PID/start-time/executable evidence, loopback-only health probes, verified owner-aware stop, orphan handoff and same-owner renewal. Studio restores historical, rerunnable and live-resumable states. Real cross-repo smoke proves bounded receipt/session completion and CLI-owner termination → external-host adoption → renewal → controlled stop. Full process recreation remains a later capability described by `docs/LOCAL_DEPLOYMENT_RUNTIME_RESUME_CONTRACT.md`, not part of the M7.6 contract. |
| M7.7 bezpieczny ESP32 flash | `Embedded.Toolchain` / `Studio` | 🔨 CLI and Studio baseline complete: Deploy Center exposes read-only PlatformIO probe with no automatic selection, explicit device selection backed by a fresh probe, exact-Build embedded packaging, a separate human flash confirmation, and deterministic selection/flash receipts. Full `JunexisStudio.sln` has `1611/1611` tests green. Completion still requires a physical ESP32 smoke with recorded receipt evidence. |
| M7.8 ciągłość Bundle ID / Build ID w Narrative | `Narrative` / `Studio` | ✅ canonical `build_id` + `bundle_id` emitted by C++/Go/TypeScript; local server/report and PlatformIO embedded upload receive the exact verified pair; Diagnostic Story 1.2, filters and Studio navigation reject cross-bundle evidence. Lang `612/612`, Narrative `326/326`, Studio `1612/1612` tests green. |
| M7.9 autentyczność i polityka System Bundle | platforma | ✅ detached ECDSA P-256/SHA-256 signature over exact `manifest.json`; explicit `unsigned/verified/trusted/invalid` evidence; `permissive/signed/trusted` admission in bundle verify, deployment preflight 0.2, local deployment, embedded flash and Studio Deploy Center. Lang `616/616`, Studio `1614/1614`; deterministic signed/trusted process E2E passes. |
| M7.10 golden system E2E | wszystkie repo | ✅ one project → native C++/Go/TypeScript package gates → signed/trusted Bundle → exact-Build non-blocking Mentor preflight → bounded server/report deployment → five runtime events → Why depth 4 → Diagnostic Story 1.2, all under one Build ID / Bundle ID; versioned evidence report 0.1. Physical ESP32 flash remains the explicit M7.7 boundary. |

---

## Milestone 8 — Semantic Conformance

**Cel:** ten sam program Junexis ma zachować to samo znaczenie na backendach
C++, Go i TypeScript. M8 stabilizuje wykonywalny podzbiór semantyki przed
rozpoczęciem VM/WASM/AOT albo składni `resource`. Szczegółowy kontrakt opisuje
`spec/22-semantic-conformance.md`.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| M8.1 ledger scenariuszy + raport `junexis.semantic-conformance/0.1` | `IR` / `CodeGen.Tests` / platforma | ✅ versioned ledger + native evidence suite + deterministic SHA-256 report; honest baseline: `chan=S1`, `queue=S2`, `stream=S2`, target S3 remains open |
| M8.2 minimalny Typed Semantic IR 0.1 | `Core` / `IR` | ✅ backend-independent types, effects, lifecycle, canonical communication operations and source origin; focused `6/6`, Compiler.Tests `242/242` |
| M8.3 wartości, typy, wyrażenia i control flow | `IR` / `CodeGen` | ✅ one native C++/Go/TypeScript equivalence test, canonical JSON result for Int/Float/Text/Bool, arithmetic/comparison/logical expressions and if/else-if/else; first S3 scenario, report `1/4` target satisfied |
| M8.4 lifecycle tasków, stan i czas | `IR` / runtime | ✅ `junexis.task-lifecycle/0.1`; bounded logical lifecycle `start → tick* → stop`, state preserved across invocations, Duration normalized to ms, first `every` tick after interval; native C++/Go/TypeScript S3, report `2/5` target satisfied |
| M8.5 zgodność `chan<T>` | C++ / Go / TypeScript | ✅ bounded FIFO capacity 1, producer waits, retention until consumed; real C++ condition-variable runtime, native Go channel and TypeScript async `Channel<T>` produce one canonical S3 result; report `3/5` target satisfied |
| M8.6 zgodność `queue<T>` + ACK/NACK | C++ / Go / TypeScript | ✅ stable Message ID, at-least-once redelivery, NACK retention, invalid-ACK no-op, removal only after valid ACK and FIFO next delivery; hidden ACK binding added to C++; native S3, report `4/5` target satisfied |
| M8.7 zgodność `stream<T>` + fanout | C++ / Go / TypeScript | ✅ latest-wins, niezależne subskrypcje, brak replay historii i duplikatu bez nowej publikacji; natywny S3, raport `5/5` target satisfied |
| M8.8 `extern`, wire contract i granica targetów | `CodeGen` / runtime | ✅ typed injected `extern` pozostaje efektem source targetu; wspólny `junexis-wire/0.2` w IR; wykonywalny Go server → TypeScript report, unknown version rejected, canonical S4; raport `6/6` |
| M8.9 status conformance w CLI, Studio i Mentorze | tooling | ✅ `junexis conformance`; Mentor schema 1.2 z nieblokującą opinią i lukami; Studio `Analysis → Mentor → Conformance`; `partial` nie zmienia wyniku kompilacji |
| M8.10 Golden Semantic E2E | wszystkie repo | ✅ raport `junexis.semantic-conformance/0.2` związany przez SHA-256 z Build ID, Bundle ID i ledgerem; CLI oraz Mentor walidują tożsamość; Studio pokazuje `BOUND`; golden test odrzuca manipulację |

**Kontrakt domknięcia:** reprezentatywne scenariusze osiągają S3, scenariusz
rozproszony osiąga S4, a raport zachowuje Build ID / Bundle ID. Wynik
`not-run` nie może być prezentowany jako `passed`.

M8 jest zamknięty dla jawnie wybranego podzbioru semantycznego. Nie oznacza to
stabilności 1.0 ani kompletności języka i runtime'u.

---

## Milestone 9 — Distributed System Topology Contract

**Cel:** kompilator ma emitować kanoniczny opis całego systemu: jego węzłów,
granic, placement, komunikacji, failure domains i źródeł. Backend oraz narzędzia
nie mogą budować własnych, rozbieżnych interpretacji topologii.

| Zadanie | Projekt | Status |
|---------|---------|--------|
| M9.1 `junexis.system-topology/0.1` | `IR` / spec | ⏳ kanoniczne węzły i krawędzie dla istniejących konstrukcji |
| M9.2 stable IDs i source origin | `IR` / analysis | ⏳ deterministyczna tożsamość tasków, pipeline'ów, granic i komunikacji |
| M9.3 placement i target boundaries | `Topology` | ⏳ walidacja `@embedded` / `@server` / `@report` bez logiki backendu |
| M9.4 communication + protocol bindings | `IR` / topology | ⏳ typowane krawędzie `chan` / `queue` / `stream` oraz jawny wire/protocol contract |
| M9.5 failure domains | `IR` / runtime | ⏳ supervisor, circuit breaker, ownership i recovery jako część grafu |
| M9.6 bound topology artifact | CLI / package | ⏳ hash grafu związany z Analysis Snapshot, Build ID i Bundle ID |
| M9.7 runtime/deployment consumer | runtime / deploy | ⏳ wykonanie i preflight konsumują ten sam manifest topologii |
| M9.8 Studio Topology Explorer | Studio | ⏳ graf, filtry, granice i nawigacja do source |
| M9.9 Mentor topology review | Mentor | ⏳ exact-Build, dyskretne i non-blocking opinie o strukturze systemu |
| M9.10 Golden Distributed System E2E | wszystkie repo | ⏳ build → topology → bundle → deploy → failure/recovery → Narrative/Observability evidence |

**Kontrakt domknięcia:** jeden hash topologii przechodzi przez analysis, package,
deployment, runtime, Studio, Mentora i dowód E2E. Scenariusz musi zawierać
rzeczywistą granicę targetów oraz kontrolowane failure/recovery.

### Powiązanie z Observability SDK (O3.1)

M9 definiuje tożsamość i kontekst systemu, które Observability SDK propaguje do
`log`, `audit`, `health` i `metric`. Implementacja po stronie języka jest
prowadzona jako O3.1b–O3.1d w `junexis-observability/docs/PRODUCER-SDK.md`:

- Semantic IR zachowuje rodzaj sygnału zamiast redukować go do tekstowego loga,
- codegen C++, Go i TypeScript emituje ten sam kanoniczny kontrakt,
- Build ID, Bundle ID, topology identity, session, trace i Narrative context są
  dziedziczone z wykonania, a nie ręcznie odtwarzane w aplikacji,
- niedostępność observability pozostaje domyślnie non-blocking; jawna polityka
  może wymagać dowodu audytowego dla deploymentu lub flashowania.

To powiązanie nie rozszerza zakresu M9 o backend storage. M9 dostarcza semantykę
i tożsamość systemu, natomiast roadmapa O odpowiada za SDK, transport, ingress,
policy i trwałość dowodów.

---

## Milestone 10 — Aplikacja TypeScript dla `@report`

**Cel:** `@report` generuje samodzielną aplikację przeglądarkową, a nie tylko
klienta TypeScript wypisującego dane do konsoli. Pełny kontrakt opisuje
`spec/23-report-application.md`.

| Zadanie | Projekt | Status |
|---|---|---|
| M10.1 `junexis.report-application/0.1` | `IR` / `CodeGen` / spec | 🔨 manifest + inventory baseline |
| M10.2 scaffold HTML/TypeScript/CSS i produkcyjny bundle | `CodeGen` | 🔨 browser bundle + bounded static server |
| M10.3 bezpieczny automatyczny renderer typów | `CodeGen` / runtime | 🔨 struct baseline, pełna macierz typów otwarta |
| M10.4 jawny lifecycle połączenia i reconnect | runtime / UI | 🔨 connected/reconnecting E2E, incompatible otwarte |
| M10.5 ograniczona historia i duże payloady | runtime / UI | 🔨 limit 50, pełna bramka obciążenia otwarta |
| M10.6 adapter własnego React/Vue/innego UI | `CodeGen` | ⏳ |
| M10.7 `junexis run`, package i deployment | CLI / platforma | ⏳ |
| M10.8 dostępność, CSP i bezpieczeństwo renderowania | UI / tests | ⏳ |
| M10.9 Studio: otwarcie panelu i diagnostyka | Studio / tooling | ⏳ |
| M10.10 Golden Home Report E2E | wszystkie repo | ⏳ |

**Kontrakt domknięcia:** pusty konsument `@report` daje użyteczny panel w
prawdziwej przeglądarce; uruchomiony serwer dostarcza dane, rozłączenie i
reconnect są widoczne, a zweryfikowana paczka działa bez checkoutu źródeł.

---

## Milestone 11 — Moduły Junexis i provider SQLite

**Cel:** projekt wieloplikowy importuje typy, funkcje i taski z bibliotek, a
pierwszy referencyjny provider trwałego stanu zapisuje dane w SQLite. Pełny
kontrakt opisuje `spec/24-modules-and-sqlite.md`.

| Zadanie | Projekt | Status |
|---|---|---|
| M11.1 tożsamość modułu i source roots | project model / spec | ✅ |
| M11.2 resolver i deterministyczny graf importów | Core / IR | ✅ |
| M11.3 symbole typów, `fn`, `task` i `extern` między plikami | Core / Semantics | ✅ |
| M11.4 scalony Semantic IR, topologia i codegen | IR / Topology / CodeGen | ✅ |
| M11.5 workspace LSP dla modułów | LspHost / tooling | ✅ |
| M11.6 lokalne biblioteki i manifest zależności | CLI / project model | ✅ |
| M11.7 `junexis.storage/0.1` | Semantic IR / runtime / spec | ✅ |
| M11.8 provider SQLite bez wymogu CGO | Go runtime / adapter | ✅ |
| M11.9 provider pamięciowy zgodności | runtime / tests | ✅ |
| M11.10 Golden Modular Storage E2E | wszystkie repo | ⏳ |

**Kontrakt domknięcia:** co najmniej cztery moduły `.jnx` budują jeden system;
SQLite wykonuje migrację, zapis, rollback i odczyt po restarcie; `@report`
widzi wynik przez zadanie `@server`, a nie przez bezpośredni dostęp do bazy.

---

## Kierunek po konsolidacji — własny model wykonania (D16)

Ten kierunek nie jest zakresem implementacyjnym 0.2. Obecne backendy
C++/Go/TypeScript pozostają podstawą działania platformy do czasu ustabilizowania
semantyki, IR, ABI i runtime contracts.

| Etap | Cel | Status |
|------|-----|--------|
| Backend conformance | Wspólne testy semantyki dla C++, Go i TypeScript | ⏳ |
| Typed Semantic IR | IR niezależny od języków hosta | ⏳ |
| Distributed Topology IR | Jawne granice, placement, protocol bindings i transport | 🟡 baseline REST/target blocks |
| Runtime ABI + wire format | `junexis-wire/0.2` dla HTTP JSON; pozostałe transporty do ujednolicenia | 🟡 |
| Junexis bytecode + VM | Docelowy przenośny model `@server` | 🔮 |
| Native AOT | Docelowy backend `@embedded`, opcjonalnie `@server` | 🔮 |
| WebAssembly | Docelowy backend `@report` | 🔮 |
| System bundle | Pakowanie artefaktów, schematów, deploymentu i Narrative map | 🔮 |

---

## Kierunek po konsolidacji — zasoby i typowane I/O (D17)

`resource` ma oddzielić zależność tasku od stanu lub I/O od konkretnej
technologii. Kierunek jest opisany w `spec/10-resources-and-io.md`, lecz nie jest
jeszcze częścią gramatyki ani obietnicą implementacyjną 0.2.

| Etap | Cel | Status |
|------|-----|--------|
| Capability model | Minimalne kontrakty storage oraz source/sink bez nazw konkretnych providerów | ⏳ |
| Failure semantics | Trwałość, transakcje, spójność, retry, backpressure i lifecycle | ⏳ |
| Binding model | Powiązanie logicznego resource z providerem w deployment | ⏳ |
| Typowany I/O | Rozdzielenie komunikacyjnego `stream<T>` od `Source<T>` / `Sink<T>` | ⏳ |
| Transformacje | Kodowanie, kompresja, archiwa i bufory pamięci jako kompozycja | ⏳ |
| Pierwszy vertical slice | Memory + SQLite od spec do testu E2E (M11) | ⏳ |
| Adapter bazodanowy | Referencyjny provider SQLite po ustaleniu semantyki transakcji | ⏳ M11 |
| Filesystem i sekwencyjne I/O | Osobny pion po storage 0.1 | 🔮 |

---

## Poza aktualnym zakresem 0.2

| Funkcja | Uzasadnienie |
|---------|--------------|
| Interaktywny REPL | Nie jest wymagany do walidacji modelu systemów rozproszonych; Junexis VM z D16 jest runtime'em bytecode, nie obietnicą REPL |
| Reflection w runtime | Sprzeczne z filozofią compile-time safety |
| Garbage collector dla @embedded | Zbyt duże narzuty na mikrokontrolery |
| Generics (user-defined) | Planowane v2.0 |
| Makra / metaprogramowanie | Planowane v2.0 |

---

## Historia kamieni milowych

| Kamień milowy | Data | Co |
|---------------|------|----|
| Pierwsza rozbudowana specyfikacja prototypu | 2026-04-06 | Pierwotnie opisana jako 1.0 stable; status później wycofany |
| Scaffold kompilatora | 2026-04-08 | 10 projektów |
| Gramatyka ANTLR4 kompiluje się | 2026-04-08 | 0 errors ANTLR4 build |
| Parser + IR + Topology gotowe | 2026-04 | M1–M3 done |
| LSP: hover, completion, definition, symbols | 2026-04 | M5 częściowo |
| Fałszywe ostrzeżenia browser naprawione | 2026-04 | severity info |
| CodeGen C++ — testy kanoniczne (5 testów) | 2026-04 | commit 94f81e2 |
| LSP hover dla pól struct/wariantów enum/params | 2026-04 | commit d20ac9f |
| IRTask.Native — propagacja native{} z task body | 2026-05-04 | commit 5b9327d |
| send stmt (scalar output) w Go i TypeScript | 2026-05-04 | — |
| Przeklasyfikowanie języka do 0.2 Experimental | 2026-06-23 | Początek konsolidacji spec → grammar → AST → semantics → IR → codegen → runtime → E2E |
