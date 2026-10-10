# Junexis Lang

**Junexis Lang** (`.jnx`) to eksperymentalny język deklaratywny dla systemów rozproszonych — od mikrokontrolerów po cloud. Jeden plik źródłowy ma opisywać cały system: embedded sensors, server backend i browser frontend. Docelowy toolchain generuje kod dla wielu platform i adaptery protokołów.

> **Wersja specyfikacji:** 0.2.0 Experimental — 2026-06-23  
> **Status:** working baseline

Specyfikacja opisuje kierunek języka, a nie gwarantowany stan implementacji.
Zobacz [oficjalny status specyfikacji](spec/STATUS.md).

Proces tworzenia samodzielnych paczek Kompilatora i LSP dla Windows, Linux
oraz macOS opisuje [dokument M19.4.2](docs/m19-4-2-multiplatform-language-tools.md).

## Co to jest

`junexis-lang` jest canonical ownerem języka Junexis: jego gramatyki,
specyfikacji, kompilatora, CLI, LSP hosta i bazowego modelu codegen.
To tutaj definiowane jest znaczenie programu `.jnx`, zanim zostanie ono
przeniesione do runtime, tooling, Narrative, Mentora i pozostałych części
platformy.

## Co działa dziś

To repo nie jest już tylko eksperymentalnym szkicem języka. Zawiera działający
rdzeń compiler toolchain: parser, AST, Structural IR, analizę topologii,
podstawowy codegen, CLI oraz LSP host z pakowalnym VSIX po stronie tooling.

Najważniejsze praktyczne fakty na dziś:

- główny przepływ `parse -> check -> analyze -> build` istnieje i jest używalny,
- project-first workflow jest już jawnym docelowym kontraktem pracy,
- istnieją reprezentatywne przykłady i powtarzalna bramka `verify-junexis-02`,
- implementacja nadal pozostaje częściowa i nierówna zależnie od targetu oraz
  poziomu dowodu E2E.

## Jak czytać to README

Pierwsza część ma odpowiedzieć na pytanie, czym jest język, co realnie działa
dziś i jak wejść do repo bez gubienia się w milestone'ach. Dalsza część
schodzi do konkretu: specyfikacji, przykładów, komend CLI, statusu
implementacji i struktury repo.

---

## Szybki start — 4 linie

```junexis
task TempSensor() @embedded -> chan<TempData>
task DataHub(input: chan<TempData>) @server -> stream<Alert>
task Dashboard(alerts: stream<Alert>) @report

pipeline Home { use TempSensor -> DataHub -> Dashboard }
```

Zamierzonym wynikiem kompilacji są: C++ dla ESP32, Go dla serwera,
TypeScript dla przeglądarki oraz mosty MQTT i WebSocket. Zakres ten nie jest
jeszcze w całości zweryfikowany E2E.

---

## Pierwsze kroki

Zacznij od jednej z trzech ścieżek:

- spec, jeśli chcesz najpierw zrozumieć model języka,
- examples, jeśli chcesz zobaczyć kształt programu `.jnx`,
- CLI, jeśli chcesz od razu sprawdzić działający compiler slice lokalnie.

**1. Przeczytaj spec:**
- [spec/STATUS.md](spec/STATUS.md) — aktualna wersja, poziomy dojrzałości i zasady weryfikacji
- [spec/00-philosophy.md](spec/00-philosophy.md) — filozofia języka i odpowiedzialności jego konstrukcji
- [spec/01-overview.md](spec/01-overview.md) — filozofia i szybki start (10 min)
- [spec/04-declarations.md](spec/04-declarations.md) — task, struct, enum, pipeline, api
- [spec/10-resources-and-io.md](spec/10-resources-and-io.md) — eksperymentalny model zasobów, baz danych, plików i typowanego I/O (D17)
- [spec/11-code-generation.md](spec/11-code-generation.md) — M4: poziomy dowodu i kontrakty artefaktów
- [spec/12-cli-and-lsp.md](spec/12-cli-and-lsp.md) — M5: kontrakt CLI, LSP i extension
- [spec/16-system-bundle.md](spec/16-system-bundle.md) — M7.1: Experimental System Bundle 0.1
- [spec/17-system-package.md](spec/17-system-package.md) — M7.2: build, native gates i zweryfikowany bundle
- [spec/18-deployment-preflight.md](spec/18-deployment-preflight.md) — M7.3: techniczna gotowość i nieblokująca opinia Mentora
- [spec/19-local-deployment.md](spec/19-local-deployment.md) — M7.4: ograniczone lokalne uruchomienie server + report
- [spec/21-golden-system-e2e.md](spec/21-golden-system-e2e.md) — M7.10: jeden zaufany przepływ package → deploy → observe
- [spec/22-semantic-conformance.md](spec/22-semantic-conformance.md) — M8: wspólne znaczenie programu na C++/Go/TypeScript
- [spec/13-compiler-runtime-baseline.md](spec/13-compiler-runtime-baseline.md) — M5.5: bramka zgodności compiler/runtime przed M6
- [spec/14-project-layout.md](spec/14-project-layout.md) — docelowy layout projektu, manifest i izolacja artefaktów
- [spec/15-narrative-runtime-trace.md](spec/15-narrative-runtime-trace.md) — M6.1: wspólny kontrakt trace dla generowanych artefaktów

**2. Przejrzyj przykłady:**
- [examples/README.md](examples/README.md) — role przykładów, zasady weryfikacji i audyt scenariusza 01
- [examples/01-temperature-monitor.jnx](examples/01-temperature-monitor.jnx) — prosty sensor → server → dashboard; scenariusz E2E do ponownej weryfikacji
- [examples/02-ecommerce-api.jnx](examples/02-ecommerce-api.jnx) — REST API + queue + multi-service; scenariusz E2E do ponownej weryfikacji
- [examples/13-minimal-pipeline.jnx](examples/13-minimal-pipeline.jnx) — minimalny pipeline; scenariusz E2E do ponownej weryfikacji
- [examples/03-smart-home.jnx](examples/03-smart-home.jnx) — pełny system: wiele urządzeń, HVAC, alerty

**3. Skonfiguruj tooling:**
```bash
# build lokalny
dotnet build compiler/JunexisLang.Compiler.slnx -v q

# check lokalny
dotnet run --project compiler/JunexisLang.Compiler.CLI -- check examples/01-temperature-monitor.jnx

# snapshot analizy przed codegenem (dla Studio/Mentora)
dotnet run --project compiler/JunexisLang.Compiler.CLI -- analyze examples/01-temperature-monitor.jnx -t server --json

# sam parser + AST, bez semantyki
dotnet run --project compiler/JunexisLang.Compiler.CLI -- parse examples/grammar/14-platform-annotations.jnx

# build lokalny (`compile` pozostaje aliasem kompatybilności)
dotnet run --project compiler/JunexisLang.Compiler.CLI -- build examples/01-temperature-monitor.jnx -t server

# walidacja E2E: check → compile → go build
dotnet run --project compiler/JunexisLang.Compiler.CLI -- validate-example examples/01-temperature-monitor.jnx
```

**Project-first workflow (zalecany dla nowych systemów):**
```bash
dotnet run --project compiler/JunexisLang.Compiler.CLI -- init my-system
cd my-system
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- check
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- build
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- run --duration 8
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- package
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- deploy preflight out/system/<bundle>.zip
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- bundle create
dotnet run --project ../compiler/JunexisLang.Compiler.CLI -- bundle verify out/system/<bundle>.zip
```

Tryb `check/build <plik.jnx>` pozostaje jako compatibility/playground i corpus
regresyjny. Nie jest docelowym wzorcem organizacji realnych systemów Junexis.
Decyzję zapisuje [D18 w ADR](spec/09-decisions.md#d18--project-first-workflow-i-tryb-plikowy-jako-compatibilityplayground).

**Powtarzalna bramka Junexis 0.2:**
```powershell
.\scripts\verify-junexis-02.ps1
```

Publiczny workflow GitHub Actions uruchamia się dla pushy, pull requestów do
`main` i ręcznie. Buduje rozwiązanie, uruchamia testy oraz waliduje trzy
reprezentatywne przykłady z wygenerowanym kodem Go.

Skrypt buduje Release CLI, parsuje korpus `examples/grammar`, sprawdza główne
przykłady, generuje artefakty dla `server`, `embedded` i `report` oraz uruchamia
bramkę project-first dla `examples/projects/01-temperature-monitor`. Uruchamia
też reprezentatywny `artifact-gate` dla `server`/`report` z lokalnym cache narzędzi.
`embedded artifact-gate` jest uruchamiany tylko wtedy, gdy dostępne jest PlatformIO
(`pio`).

Jeśli aktywny LSP z VS Code blokuje pliki `bin/Release`, zrestartuj language
server/window i uruchom skrypt ponownie. Opcja `-SkipBuild` służy tylko do
świadomej walidacji już zbudowanego CLI.

Instalacja jako globalne `dotnet tool` nie jest jeszcze opisana jako gotowa ścieżka produkcyjna w tym repo.

CLI może być także opublikowane jako wersjonowany backend self-contained dla
Studio i rozszerzenia VS Code:

```powershell
.\scripts\publish-cli.ps1 `
  -RuntimeIdentifier win-x64 `
  -SelfContained `
  -OutputPath .\publish\cli\win-x64
```

Dystrybucja zawiera `junexis-cli.json` z protokołem 2, który gwarantuje obsługę
wyniku `build --json --output-schema 2.0` i kontraktu `latest-build` 1.0.

---

## Status projektu

| Obszar | Status |
|--------|--------|
| Specyfikacja języka | ⚗️ 0.2.0 Experimental |
| Gramatyka ANTLR4 + AST | ✅ M1: 27/27 plików obu korpusów buduje AST |
| Structural IR | ✅ M2: 27/27 plików przechodzi bezstratną transformację AST → IR |
| Logiczna topologia | ✅ M3: port-aware graph, SCC, fan-in/out i depth dla 13/13 głównych przykładów |
| Code generation | ✅ M4 baseline: L1 13/13; L2 embedded 10/10, server 12/12, report 4/4; wąskie, wykonywalne L3 i L4 |
| CLI `parse/check/analyze/build/run` | ✅ M5 + bezpieczna, ograniczona sesja projektu `@server` |
| Go codegen (`@server`) | 🟡 Partial; REST/OpenAPI, bridge runtime oraz wykonywalne ścieżki L3/L4 |
| C++ codegen (`@embedded`) | 🟡 Partial |
| TypeScript codegen (`@report`) | 🟡 Partial |
| Compiler/runtime conformance | ✅ M5.5: wąskie R4, 423 testy compiler/LSP, 540/540 runtime .NET, CTest 2/2 embedded C |
| E2E | 🟡 Reprezentatywna ścieżka Go → TypeScript przez `junexis-wire/0.2`; nie jest jeszcze pełną macierzą stabilności |
| LSP Host + VS Code extension | ✅ M5 baseline C3: pełna sesja stdio, semantic tokens i pakowalny VSIX |

---

## Filozofia

**Trzy typy komunikacyjne — jeden wybór**

| Typ | Semantyka | Kiedy |
|-----|-----------|-------|
| `chan<T>` | Point-to-point z kontrolą przepływu; dokładna gwarancja zależy od runtime i transportu | RPC, control plane |
| `queue<T>` | At-least-once, trwały | Eventy, IoT data |
| `stream<T>` | Latest-wins, może dropować | Telemetria, dashboardy |

**Trzy platformy — jeden plik źródłowy**

| Adnotacja | Target | Generowany kod |
|-----------|--------|----------------|
| `@embedded` | ESP32, STM32, RP2040, ... | C++/PlatformIO |
| `@server` | Linux, Cloud, Docker | Go / C# |
| `@report` | Browser, React Native | TypeScript/React |

**Docelowy zakres automatycznej generacji:**
- Adaptery protokołów (MQTT, gRPC, WebSocket, REST)
- Serializację/deserializację (Protobuf, MessagePack, JSON)
- Docker Compose i deployment configs
- OpenAPI 3.0 spec dla każdego REST API

---

## Struktura repozytorium

```
junexis-lang/
├── spec/                       # Specyfikacja języka
│   ├── STATUS.md               # Aktualna wersja i poziomy dojrzałości
│   ├── 00-philosophy.md        # Filozofia i podział odpowiedzialności
│   ├── 01-overview.md          # Filozofia, quick start, podstawy
│   ├── 02-lexical.md           # Tokeny, komentarze, białe znaki
│   ├── 03-types.md             # System typów: primitives, struct, enum
│   ├── 04-declarations.md      # task, device, config, api, pipeline...
│   ├── 05-communication.md     # chan, queue, stream — semantyka
│   ├── 06-control-flow.md      # loop, match, every, if, fn
│   ├── 07-platform.md          # @embedded, @server, @report, device
│   ├── 08-protocols.md         # MQTT, gRPC, WebSocket, REST, AMQP
│   ├── 09-decisions.md         # ADR — uzasadnienia decyzji projektowych
│   ├── 10-resources-and-io.md  # Projekt resource, storage i sekwencyjnego I/O
│   ├── 11-code-generation.md   # M4: kontrakty i bramki artefaktów
│   ├── 12-cli-and-lsp.md        # M5: CLI, LSP i extension
│   ├── 13-compiler-runtime-baseline.md # M5.5: conformance compiler/runtime
│   └── 14-project-layout.md     # Manifest projektu i izolacja artefaktów
├── grammar/
│   └── JunexisLang.g4          # Kanonyczna gramatyka ANTLR4
├── examples/
│   ├── 01-temperature-monitor.jnx   # Prosty IoT monitor
│   ├── 02-ecommerce-api.jnx         # REST API + message queue
│   └── 03-smart-home.jnx            # Pełny system smart home
└── README.md
```

---

## Dalsze informacje

- [ROADMAP.md](ROADMAP.md) — plan implementacji kompilatora (milestones M1–M6)
- [CHANGELOG.md](CHANGELOG.md) — historia zmian
