# Changelog — junexis-lang

Format: [Keep a Changelog](https://keepachangelog.com/pl/1.1.0/)  
Wersjonowanie: [Semantic Versioning](https://semver.org/spec/v2.0.0.html)

---

## [Unreleased]

### 2026-10-10 — publiczna bramka CI

- Zastąpiono Jenkins Multibranch workflow GitHub Actions uruchamianym dla pushy,
  pull requestów i ręcznie. Workflow przywraca zależności, buduje rozwiązanie,
  uruchamia testy i waliduje trzy reprezentatywne przykłady przez Go.
- Dodano licencję MIT.
- Usunięto z README odwołania do sąsiednich, niedostępnych repozytoriów oraz
  wewnętrznych instrukcji. Testy CLI wykrywają katalog repozytorium po pliku
  rozwiązania, bez wymagania obecności `AGENTS.md`. Dokumentacja walidacji
  wskazuje workflow GitHub Actions. Przykłady używają zanonimizowanych nazw
  brokerów. CLI zachowuje możliwość użycia wspólnego schematu Registry z
  sąsiedniego SDK, a publiczny checkout ma lokalny zapasowy egzemplarz kontraktu.

### 2026-09-25 — wspólny adres panelu

Wyjątek właściciela: ta implementacja może powstać bez issue. Zakres obejmuje
przekazywanie gotowego adresu CLI → Studio / VS Code, nadzór sesji i testy.
Obsługa `junexis.panel-ready/1` dodaje panel server-only z deklaracji aplikacji,
zachowując starszy komunikat report. Brak deklaracji nie tworzy fikcyjnego adresu.

### Added
- Dodano autoryzowaną przez właściciela 2026-09-10 bez issue granicę wyboru
  wariantu M19.10.1. `junexis.packages.lock/0.1` zachowuje wyłącznie logiczne
  `id + version`, natomiast `PlatformVariantSelector` wybiera dokładny artefakt
  dla RID-u ze zweryfikowanego katalogu M19 bez zmiany projektu lub blokady i
  bez powrotu do wariantu innego systemu.
- Dodano autoryzowaną przez właściciela 2026-09-01 bez issue emisję zdarzeń
  klienta Registry M17.10.3. `restore --registry` przekazuje jeden
  `X-Correlation-Id` przez odczyt migawki, przekierowania i transfer pakietu,
  raportuje początek, wynik oraz czas operacji według kontraktu SDK i nie
  ujawnia ścieżki ani treści projektu.
- Dodano autoryzowany przez właściciela 2026-09-01 bez issue tryb offline
  pakietów aplikacyjnych M17.9.2. Udany restore z Registry utrwala pełny
  zweryfikowany snapshot i rachunek lokalnego cache M17.9.1 w tej samej
  transakcji co pakiety oraz `junexis.packages.lock`; późniejsze `restore`,
  `check` i `build` nie uruchamiają DNS ani HTTP i odrzucają niespójne
  metadane, rozmiar lub SHA-256.
- Dodano autoryzowaną przez właściciela 2026-09-01 bez issue odporność klienta
  Registry M17.8.2 zgodną z polityką SDK. Bezpieczne żądania obsługują
  ograniczone ponowienia, `Retry-After`, timeouty i przerwane transfery;
  staging jest cofany przed kolejną próbą. Tabelaryczne testy `404`, `429`,
  `5xx`, utraty połączenia, przekroczenia rozmiaru, anulowania i współbieżności
  potwierdzają niezmienność pamięci podręcznej i `junexis.packages.lock`.
- Dodano autoryzowany przez właściciela 2026-08-31 bez issue publiczny wybór
  istniejącego źródła Registry dla M17.5.4. `junexis-compiler restore
  --registry <adres>` przekazuje zweryfikowany snapshot do tego samego resolvera
  dokładnych wersji; opcja nie łączy się z `--offline`.
- Dodano autoryzowaną przez właściciela 2026-08-31 bez issue bramkę M17.4.4.
  Integracyjne testy pełnej ścieżki klienta HTTP potwierdzają, że timeout,
  przerwane połączenie i anulowanie zachowują bajtowo ostatni poprawny cache
  oraz `junexis.packages.lock`, nie pozostawiając stagingu, backupu ani journalu.
  Dwa równoległe procesy restore, z których drugi wykonuje komendę CLI i jest
  odrzucany przez blokadę pierwszego, także zachowują stan, który następnie
  przechodzi restore offline.
- Dodano autoryzowane przez właściciela 2026-08-30 bez issue mapowanie snapshotu
  Registry M17.4.3. `RegistrySnapshotPackageSource` weryfikuje `snapshotId`,
  zamknięty kontrakt JSON, limity, tożsamości, dokładne zależności i zgodność,
  pomija wpisy providerów oraz pobiera pakiety przez endpoint tożsamości
  `/v1/artifacts/package/{id}/{version}/content`. Snapshot i manifest muszą być
  zgodne, a lokalne i zdalne źródło tworzą bajtowo identyczny lockfile.
- Dodano autoryzowany przez właściciela 2026-08-30 bez issue bezpieczny klient
  pobierania M17.4.2. Klient strumieniuje odpowiedzi HTTP z limitami indeksu i
  artefaktu, ograniczonym timeoutem i anulowaniem oraz ręcznie dopuszcza najwyżej
  trzy przekierowania HTTPS bez zmiany authority. Przerwany lub nadmiarowy
  transfer nie promuje cache i nie zapisuje `junexis.packages.lock`.
- Dodano autoryzowaną przez właściciela 2026-08-30 bez issue granicę źródła pakietów
  M17.4.1: `IPackageSource`, domyślny adapter lokalny i adapter sieciowy
  delegujący transport do `INetworkPackageSourceClient`. Resolver zachowuje
  algorytm grafu, transakcję cache i format `junexis.packages.lock/0.1`, a test
  potwierdza bajtowo identyczny lockfile dla obu źródeł. HTTP i jego polityki
  bezpieczeństwa pozostają zakresem M17.4.2.
- Dodano autoryzowany bez issue pierwszy pion pakietowy: `packages` i
  `packageIndex` w manifeście projektu, `junexis restore [--offline]`, lokalny
  cache, pełną walidację SHA-256, zależności z dokładnymi wersjami,
  deterministyczny `junexis.packages.lock` i import przez wspólny katalog
  modułów. Bramka między repozytoriami usuwa indeks przed ponownym restore i
  kontrolą projektu offline oraz potwierdza identyczny lockfile. Zmiana nie
  modyfikuje gramatyki ani semantyki `.jnx`.
- Smart Home Laboratory stage `Przejmij kontrolę`: explicit effective runtime
  configuration, automatic and operator-approved recovery, bounded evidence
  retention, causal recovery events and cancellation-safe reset.
- Smart Home Laboratory stage `Eksperymentuj`: runtime controls for the air
  quality threshold, communication delay and sensor-loss policy, with revisioned
  causal events and deterministic return to the safe `Poznaj` profile.
- CLI `junexis lab package`, `lab verify` i `lab run` oraz kontrakt
  `junexis.laboratory-package/0.1`: samodzielna paczka Windows x64 zawiera
  natywny serwer, opis laboratorium, instrukcję i zweryfikowany System Bundle.
- Analysis Snapshot 1.1 with semantic relations for pipeline membership,
  Narrative, supervision, resilience protection and platform placement.
- M4 generated artifact contract with `G0001`/`G0002` diagnostics and coverage for all 13 main examples.
- CLI `junexis artifact-gate` for native PlatformIO, Go and TypeScript builds.
- CLI `junexis deploy embedded probe`, `select` i `flash` jako baseline M7.7:
  wykrywa PlatformIO, wiąże jawnie wybrane urządzenie w deterministic selection contract
  i uploaduje verified embedded bundle wyłącznie przez ten explicit handoff,
  zapisując deterministic flash receipt.
- Self-contained bootstrap project inventories for embedded, server and report.
- Native L2 conformance for all applicable main-example artifacts: embedded
  10/10, server 12/12 and report 4/4.
- Executable Go L3 conformance test for task behavior with an injected `extern` implementation.
- Semantic diagnostic `E0023` for callable argument-count mismatch.
- Publiczny kontrakt parsera: `JunexisCompiler.ParseAsync()` i `ParseSource()` zwracające `ParseResult` bez uruchamiania semantyki.
- CLI: `junexis parse <plik.jnx>` oraz automatyczne testy wszystkich 13 przykładów głównych i 14 przykładów gramatyki.
- D17 i `spec/10-resources-and-io.md`: eksperymentalny model `resource`, baz danych, plików oraz typowanego I/O, rozdzielony od komunikacyjnego `stream<T>`.
- M2: kontrolowany `IRTransformResult`, diagnostyki `R0001`/`R0002` i source map Structural IR.
- Testy AST → IR dla wszystkich 13 przykładów głównych i 14 przykładów gramatyki.
- M3: port-aware `PipelineGraph`, głębokość grafu kondensacji SCC oraz diagnostyki topologii `T0001`–`T0005`.
- Testy pełnej ścieżki `.jnx → AST → IR → topology` dla 13 głównych przykładów.
- Raport `docs/TASK-I-TASK-J-RECONSTRUCTION-2026-06-30.md` oraz roboczą, dwustopniową rekonstrukcję `Task I` i `Task J` na branchu `feat/task-i-j-reconstruction`.

### Changed
- Walidacja manifestu pakietu stosuje dla `capabilities` ten sam ścisły wzorzec
  identyfikatora co publiczny schemat SDK.
- Go Narrative tracing correlates channel `COMM_SEND` and `COMM_RECV` events
  with the same SHA-256 payload token while keeping payload contents outside
  TraceCollector; the smart-home laboratory exposes verified `/why` evidence
  and remains fail-open without a collector.
- Embedded codegen resolves device aliases and emits shared types, config, runtime and PlatformIO metadata.
- Go always emits `go.mod`; report codegen always emits runtime, manifest and TypeScript configuration with bound type imports.
- Main examples with structured hardware reads use explicit `extern task`
  signatures instead of undeclared magic functions.
- Go and TypeScript emit typed, injectable `extern` boundaries; undeclared calls are no longer hidden by inferred codegen stubs.
- `ROADMAP.md` now marks M7.4 local deployment as complete after verification against the CLI contract tests and the cross-repo Go + TypeScript smoke flow.
- Globalna nazwa narzędzia CLI została ujednolicona z `junexis-compile` do `junexis`.
- Diagnostyki składni i budowy AST mają osobną przestrzeń kodów: `P0001` i `P0002`.
- M2 definiuje bezstratny Structural IR; Typed Semantic IR, Topology IR i platform lowering pozostają osobnymi etapami D16.
- Domyślne wartości pól/parametrów i inicjalizatory stanu w IR przechowują pełne `IRExpr` zamiast wyłącznie literałów.
- Fan-in i fan-out są liczone po unikalnych logicznych endpointach, a nie po surowej liczbie powtórzonych krawędzi.
- CLI `check`, `compile`, `deploy` i `validate-example` uruchamiają analizę logicznej topologii po Structural IR.

### Fixed
- Go extern discovery now covers calls in `return`, `respond`, `delay`, `recv` and `match` branches.
- Go scalar functions with an explicit `return` no longer emit an unused synthetic `result` variable.
- Built-in `Time`, `Duration`, `Symbol` and `epoch` are recognized consistently; C++ emits concrete `now`/`epoch` helpers.
- Corrected mismatched extern return/parameter contracts in the HFT and smart-home examples.
- `ASTBuilder` nie pomija już po cichu deklaracji, dla których nie zbudował węzła.
- Błędy budowy AST są zwracane jako diagnostyki zamiast nieobsłużonych wyjątków.
- Usunięto nieosiągalną regułę gramatyki `rangeExpression`.
- Literały czasu i pamięci są reprezentowane przez `IRUnitValue`, a inicjalizatory takie jak `now()` i `epoch()` nie są już gubione.
- Eksperymentalna platforma bez profilu emituje `W5000` zamiast przerywać `check` wyjątkiem.
- Dangling endpoint pipeline emituje `T0002` zamiast tworzyć pozorny task w grafie.
- Diagnostyka cyklu pokazuje rzeczywistą ścieżkę krawędzi i nie koliduje już z platformowym `W5001`.

### Validation
- Smart Home Laboratory: rzeczywista ścieżka `lab package -> verify -> run`
  utworzyła paczkę `preview-1` dla `win-x64`, potwierdziła cztery pliki payloadu
  i uruchomiła panel bez checkoutu źródeł oraz bez kompilacji w czasie startu.
- M7.4: uruchomiono lokalnie `LocalDeploymentTests` (4/4 PASS) oraz cross-repo smoke `scripts/test-local-deployment-e2e.ps1` (PASS), potwierdzając zweryfikowany bundle/receipt/preflight, bounded session `server + report` i cleanup własnych procesów.
- M7.5: zweryfikowano w `junexis-studio` focused host/extension coverage dla Deploy Center, pełny `JunexisStudio.sln` (`dotnet build` PASS, `dotnet test` 1607/1607 PASS) oraz realny smoke startu `src/Shell`; preparation odrzuca rozjazd Build ID/Bundle ID/targetów pomiędzy verified receipt, package receipt i preflight, a audyt otrzymuje także failure receipt. Realny project-first flow `examples/projects/33-cnc` przeszedł `build -> package -> deploy preflight -> deploy local` dla `server/report` z gotowością techniczną `ready` i bounded cleanup.
- M7.6: `deploy local` publikuje trwały `deployment-receipt.json` oraz `deployment-session.json`; kontrakt sesji 0.2 niesie owner lease, heartbeat, PID długotrwałego hosta oraz procesowe evidence targetów (PID + czas startu + executable). `status` dopuszcza probe tylko do loopback `/health/ready`, `stop` odmawia zabicia procesu bez pełnej zgodności evidence, a `adopt` rozróżnia przejęcie obcego lease od odnawiania lease przez tego samego hosta. Studio odtwarza ostatni snapshot, pokazuje `Live resumable session`, odnawia własny lease w tle i zatrzymuje owned session przez canonical backend. Focused validation: trait `M7.6` w `LocalDeploymentTests` 13/13 PASS, `DeployCenterViewModelTests` 8/8 PASS oraz `JunexisDeploymentServiceTests` 10/10 PASS. Rozszerzony realny `scripts/test-local-deployment-e2e.ps1` potwierdza final receipt/session, kompletne process evidence oraz rzeczywisty handoff `CLI owner terminated → external host adopt → same-owner renew → owner-aware stop`. Smoke ujawnił i zamknął kolizję zakresu portów bridge przez atomowy wybór ciągłego zestawu HTTP/health/TCP. M7.6 jest zamknięte; pełne odtwarzanie zakończonego procesu pozostaje późniejszą funkcją opisaną w `docs/LOCAL_DEPLOYMENT_RUNTIME_RESUME_CONTRACT.md`.
- M7.7 baseline: focused `EmbeddedDeploymentHandlerTests` 2/2 PASS pokrywają realny chain `package -> select -> flash` na verified embedded bundle z fake PlatformIO, a wcześniejszy realny smoke `dotnet run --project compiler/JunexisLang.Compiler.CLI -- deploy embedded probe --json --platformio-exe .\.m4-platformio\Scripts\pio.exe` potwierdził wykrycie lokalnego PlatformIO i uczciwy wynik `no candidate embedded devices were detected` bez side effectów, gdy host nie widzi urządzenia.
- Task I: potwierdzono lokalnie, że generated Go dla `03-smart-home` emituje trace do realnego TraceCollectora po dopasowaniu payloadu ingest (`kind` jako string event kind oraz zachowanie `attributes.narrative_id`).
- Task I: zweryfikowano, że ścieżka `generated Go -> TraceCollector -> Why API` działa lokalnie; przy samych startup trace events aktualna głębokość chain pozostaje `1`.
- Task J: osiągnięto minimalny causal chain dla generated Junexis Go services bez dodawania logiki domenowej przez współdzielony `payloadHash` i pary `COMM_SEND` / `COMM_RECV`.
- Task J: poprawiono orkiestrację tasków zwracających prymitywy komunikacyjne oraz zaktualizowano golden tests codegenu Go pod nowy kontrakt helpera trace.
- Task J: walidacja lokalna przeciwko realnemu TraceCollectorowi zapisała `9` eventów, a Why API zwróciło causal chain z `depth >= 2`, np. `COMM_SEND (taskId=1, payloadHash=startup-1) -> COMM_RECV (taskId=2, payloadHash=startup-1)`.
- Rekonstrukcja robocza: branch `feat/task-i-j-reconstruction` porządkuje te wyniki do dwóch czytelnych commitów logicznych (`Task I`, potem `Task J`), bez twierdzenia, że są to exact historical states z zachowanej historii git.

## [0.2.0-experimental] — 2026-06-23

### Status
- Oficjalny stan języka został ustalony jako **Junexis Lang 0.2.0 Experimental**.
- Wycofano deklarację stabilności specyfikacji 1.0; wcześniejsze materiały są traktowane jako dokumentacja prototypu i kierunku projektu.
- `CORE` oznacza teraz element tożsamości projektowanego języka, a nie gwarancję kompatybilności ani kompletność implementacji.
- Wprowadzono jawny model dowodowy: specyfikacja → gramatyka → AST → semantyka → IR → codegen → runtime → E2E.

### Foundations
- `spec/STATUS.md` — oficjalny status, poziomy dojrzałości i warunki przyszłej stabilizacji.
- `spec/00-philosophy.md` — filozofia języka oraz podział odpowiedzialności jego konstrukcji.

### Added
- `AGENTS.md` — plik kontekstowy dla agentów AI
- `CHANGELOG.md` — ten plik
- `ROADMAP.md` — plan rozwoju kompilatora
- Gramatyka: usunięto tokeny `REST | GRPC | WS | MQTT | COAP | AMQP` z reguły `platformTarget` (sprzeczne z D6)
- Gramatyka: `typeReference` refaktoryzacja — usunięto `unionType` jako osobną regułę; zastąpiono przez `atomicType (PIPE atomicType)*` (eliminuje wzajemną lewą rekurencję)
- Gramatyka: `atomicType` jako nowa reguła pośrednia
- Build: `Core.csproj` wskazuje na `../../grammar/JunexisLang.g4` (jedno źródło prawdy); lokalna kopia `Grammar/JunexisLang.g4` usunięta

### Changed
- Examples: wszystkie `3/3` kanoniczne przykłady (`01-temperature-monitor`, `02-ecommerce-api`, `03-smart-home`) przechodzą aktualny `CLI check` po dopasowaniu do realnego kontraktu parsera/CLI.
- CLI: komenda `lsp` deleguje do `JunexisLang.Compiler.LspHost` zamiast kończyć się stubem błędu.
- LSP Host: wspólne uruchamianie przeniesione do `LspHostRunner`, a host rejestruje też `DocumentSymbolHandler` dla outline/document symbols.
- LSP Host: dodano referencje i rename w otwartych dokumentach, a także
  narrative code actions zamieniające `&&`/`||` na `and`/`or` poza tekstami
  i komentarzami.

### Fixed
- Security: nadpisano podatne tranzytywne `Microsoft.Build.Utilities.Core 17.8.3` do bezpiecznej wersji `17.8.43` w `JunexisLang.Compiler.Core.csproj`, co usuwa warning `NU1903` z solution build.
- CLI: usunięto warningi `VSTHRD103` w handlerach `check` i `compile` przez przejście z synchronicznych `Console.WriteLine` na `WriteLineAsync`.
- Task I: naprawiono lokalną integrację generated Go z TraceCollectorem przez dopasowanie payloadu trace do realnego kontraktu ingest.
  - Generated Go: zmieniono `kind` z wartości numerycznej na string event kind zgodny z TraceCollectorem.
  - Generated Go: zachowano `attributes.narrative_id` w emitowanym payloadzie `TraceEvent`.
  - Walidacja: potwierdzono, że generated `03-smart-home` emituje trace do realnego TraceCollectora.
  - Walidacja: potwierdzono, że `/events/{id}/why` zwraca causal chain lokalnie.
  - Wynik: `PASS` — generated Go -> TraceCollector -> Why API działa lokalnie.
  - Uwaga: obecna głębokość chain wynosi `1`, ponieważ generated Go emituje tylko startup trace events; głębsze chain wymagają sparowanych `SEND/RECV` albo korelacji przez `payloadHash`.
- Task J: osiągnięto minimalny causal chain dla generated Junexis Go services bez dodawania logiki domenowej.
  - Generated Go: dodano startup trace correlation przez współdzielone `payloadHash`.
  - Generated Go: dodano pary eventów `COMM_SEND` i `COMM_RECV` potrzebne do rekonstrukcji przez HB index.
  - Generated Go: poprawiono orkiestrację tasków zwracających prymitywy komunikacyjne, aby startup nie blokował uruchomienia kolejnych tasków.
  - Testy: zaktualizowano golden tests codegenu Go pod nowy kontrakt helpera trace.
  - Walidacja: lokalne uruchomienie przeciwko realnemu TraceCollectorowi zapisało `9` eventów.
  - Walidacja: Why API zwróciło causal chain z `depth >= 2` rekonstruowany przez dopasowanie `payloadHash`.
  - Przykład: `COMM_SEND (taskId=1, payloadHash=startup-1) -> COMM_RECV (taskId=2, payloadHash=startup-1)`.
  - Wynik: pierwszy end-to-end causal reconstruction path z `.jnx -> generated Go -> runtime execution -> TraceCollector -> HBIndex -> Why API` jest operacyjny.

---

## [0.2.0-prototype] — 2026-04-08

### Added
- Scaffold kompilatora: 10 projektów C#/.NET 9 (`JunexisLang.Compiler.slnx`)
  - `Core` — parser ANTLR4, ASTBuilder (placeholder)
  - `IR` — IRGenerator, IRDocument (placeholder)
  - `Topology` — analiza grafu pipeline (placeholder)
  - `Narrative` — obsługa bloku narrative (placeholder)
  - `CodeGen` + `CodeGen.Tests` — generatory kodu (placeholder)
  - `Tests` — testy jednostkowe (placeholder)
  - `CLI` — interfejs wiersza poleceń (placeholder)
  - `LspHost` — Language Server Protocol, stdio (placeholder)
  - `Embedded.Toolchain` — narzędzia deploy embedded (placeholder)

---

## [0.1.0] — 2026-04-06  `tag: v0.1.0-spec`

### Added
- Pierwsza rozbudowana specyfikacja prototypu, opisana wówczas jako v1.0.0 CANONICAL/STABLE; deklaracja ta została wycofana 2026-06-23.
  - `spec/01-overview.md` — przegląd, zakres prototypu i trzy założenia fundamentalne
  - `spec/02-lexical.md` — tokeny, literały, słowa kluczowe
  - `spec/03-types.md` — system typów: high-level, sized, złożone, komunikacyjne
  - `spec/04-declarations.md` — task, struct, enum, fn, pipeline, device, import, protocol
  - `spec/05-communication.md` — semantyki chan/queue/stream, diagnostyki W4201–W4212
  - `spec/06-control-flow.md` — if/else, for, match, every, delay, lifecycle hooks
  - `spec/07-platform.md` — platformy @embedded/@server/@report, możliwości, ograniczenia
  - `spec/08-protocols.md` — deklaracja protocol, tabela domyślnych adapterów
  - `spec/09-decisions.md` — ADR D1–D13, historia zmian spec
  - `spec/diagnostics.csv` — rejestr kodów diagnostycznych E/W/I
- Gramatyka ANTLR4 `grammar/JunexisLang.g4` — kanoniczna gramatyka języka
- Przykłady `.jnx`:
  - `examples/01-temperature-monitor.jnx`
  - `examples/02-ecommerce-api.jnx`
  - `examples/03-smart-home.jnx`
- `README.md` — dokumentacja użytkownika
