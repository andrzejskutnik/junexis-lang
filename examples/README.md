# Przykłady Junexis Lang 0.2

> **Status:** materiał eksperymentalny  
> **Zasada:** przykład jest wejściem do weryfikacji, a nie dowodem działania funkcji

## Bieżąca klasyfikacja projektów

Przegląd z 2026-09-24 obejmuje 17 projektów: 16 przechodzi `check`, a
modular-storage-golden ma
bloker parsera. Aktualny dowód wykonania dotyczy smart-home-lab na Windows.
Historycznych wyników poniżej nie należy rozszerzać na dzisiejsze źródła.

## Wspólny kontrakt jakości

**Każdy główny przykład systemu powinien działać albo być bliski działania.**
Różne role przykładów określają, co dany plik sprawdza, ale nie są
usprawiedliwieniem dla pozostawienia go w stanie uszkodzonym.

Minimalny kontrakt głównych przykładów `01`–`13`:

1. parser i AST nie mogą zakończyć procesu nieobsłużonym wyjątkiem,
2. `CLI check` musi zakończyć się sukcesem bez nieoczekiwanych diagnostyk,
3. `CLI compile` musi wygenerować artefakty dla wszystkich platform użytych w pliku,
4. wygenerowany kod powinien się budować albo mieć jeden jawny, konkretny blocker,
5. komentarze w `.jnx` muszą opisywać rzeczywiste zachowanie toolchaina,
6. przykład nie może otrzymać etykiety `VERIFIED` bez powtarzalnego testu,
7. znany brak musi być opisany obok przykładu i powiązany z pracą naprawczą.

Przykłady celowo niepoprawnego kodu powinny znajdować się w osobnym katalogu
`negative/`, mieć wskazaną oczekiwaną diagnostykę i również nie mogą powodować
awarii kompilatora.

### Osobny kontrakt `grammar/`

Katalog `grammar/` jest pozytywnym korpusem składni, która istnieje albo powinna
istnieć w Junexis Lang. Pliki z tego katalogu:

1. muszą być poprawne leksykalnie i składniowo,
2. muszą przejść parser bez błędu,
3. nie mogą powodować awarii podczas budowania reprezentacji składniowej,
4. nie muszą przechodzić analizy semantycznej,
5. nie muszą generować kodu ani być wykonywalne.

CLI udostępnia osobną komendę `parse`, dlatego walidacja `grammar/` powinna
kończyć się po parserze/AST i pozostać oddzielona od walidacji przykładów
systemowych.

### Poziomy gotowości przykładów

| Poziom | Znaczenie |
|--------|-----------|
| `VERIFIED` | Pełna deklarowana ścieżka działa i ma powtarzalny test. |
| `NEAR-RUNNABLE` | `check` i `compile` działają; pozostał mały, jawnie opisany blocker buildu albo runtime. |
| `BROKEN` | Parser, AST, semantyka lub codegen zawodzą; stan przejściowy wymagający naprawy. |

Docelowo katalog pozytywnych przykładów może zawierać wyłącznie pozycje
`VERIFIED` i `NEAR-RUNNABLE`. `BROKEN` jest sygnałem pracy, nie akceptowanym
stanem końcowym.

## Baseline audytu — 2026-06-30

### Powtarzalna bramka 0.2

Aktualny stan sprawdzamy poleceniem z katalogu `junexis-lang`:

```powershell
.\scripts\verify-junexis-02.ps1
```

Wynik referencyjny z 2026-06-30:

- **14/14** plików `examples/grammar/*.jnx` przechodzi `CLI parse`,
- **13/13** głównych przykładów przechodzi `CLI check`,
- **13/13** głównych przykładów przechodzi `CLI compile` dla targetów
  `server`, `embedded` i `report`,
- projekt `examples/projects/01-temperature-monitor` przechodzi
  `doctor`, `check`, `build --out <audit-dir>` i `clean --dry-run`,
- `artifact-gate server` przechodzi dla **12/12** przykładów, które faktycznie
  generują artefakty server,
- `artifact-gate report` przechodzi dla **4/4** przykładów, które faktycznie
  generują artefakty report,
- `artifact-gate embedded` przechodzi dla **10/10** przykładów, które faktycznie
  generują artefakty embedded, gdy dostępne jest PlatformIO (`pio`) i używane są
  krótkie ścieżki `TEMP/TMP` oraz `PLATFORMIO_CORE_DIR`.

Sukces `compile` oznacza, że komenda zakończyła się poprawnie. Nie zawsze oznacza
to wygenerowanie plików dla danego targetu — przykłady bez tasków danej platformy
kończą się poprawnym `warning: no files generated`.

### Macierz głównych przykładów `01`–`13`

Legenda:

- `yes` — target wygenerował artefakty i, jeśli dotyczy, przeszedł gate,
- `none` — target nie ma pasujących tasków i nie generuje plików,
- `n/a` — nie sprawdzano, bo target nie generuje plików,
- `T0001` — ostrzeżenie topologii o logicznym cyklu pipeline.

| Przykład | `check` | Warningi | Gen server | Gen embedded | Gen report | Gate server | Gate report | Gate embedded | Poziom |
|----------|---------|----------|------------|--------------|------------|-------------|-------------|---------------|--------|
| `01-temperature-monitor` | pass | none | yes | yes | yes | yes | yes | yes | `NEAR-RUNNABLE` |
| `02-ecommerce-api` | pass | `T0001` | yes | none | yes | yes | yes | n/a | `NEAR-RUNNABLE` z ostrzeżeniem topologii |
| `03-smart-home` | pass | none | yes | yes | yes | yes | yes | yes | `NEAR-RUNNABLE` |
| `04-temperature-basic` | pass | none | yes | yes | none | yes | n/a | yes | `NEAR-RUNNABLE` |
| `05-simple-sensor` | pass | none | none | yes | none | n/a | n/a | yes | `NEAR-RUNNABLE` |
| `06-sensor-test` | pass | none | yes | yes | none | yes | n/a | yes | `NEAR-RUNNABLE` |
| `07-user-service` | pass | none | yes | none | none | yes | n/a | n/a | `NEAR-RUNNABLE` |
| `08-hft-trading` | pass | none | yes | none | none | yes | n/a | n/a | `NEAR-RUNNABLE` |
| `09-smart-home-iot` | pass | none | yes | yes | none | yes | n/a | yes | `NEAR-RUNNABLE` |
| `10-smart-home-coap-mqtt` | pass | none | yes | yes | none | yes | n/a | yes | `NEAR-RUNNABLE` |
| `11-smart-home-simple` | pass | none | yes | yes | none | yes | n/a | yes | `NEAR-RUNNABLE` |
| `12-sensor-pipeline` | pass | none | yes | yes | yes | yes | yes | yes | `NEAR-RUNNABLE` |
| `13-minimal-pipeline` | pass | none | yes | yes | none | yes | n/a | yes | `NEAR-RUNNABLE` |

`T0001` w `02-ecommerce-api.jnx` odpowiada celowo opisanemu cyklowi
`OrderService → InventoryService → FulfillmentService → OrderService`. Nie jest
to awaria kompilatora, ale obecny język nie ma jeszcze składni/semantyki do
jawnego oznaczenia akceptowanego cyklu biznesowego. Do rozważenia pozostaje
przyszła adnotacja lub kontrakt topologii odróżniający cykl zamierzony od
potencjalnego deadlocka.

Ten wynik klasyfikuje główne przykłady jako kandydatów `NEAR-RUNNABLE`, nie
`VERIFIED`. Dla `VERIFIED` potrzebujemy jeszcze jawnej ścieżki E2E, szczególnie
dla mostów międzyplatformowych, runtime i embedded toolchaina.

Na Windows embedded gate wymaga krótkich ścieżek dla tymczasowego katalogu
artefaktów i PlatformIO core. Przy długich ścieżkach repozytorium toolchain
ESP32/RISC-V może kończyć się błędem `riscv32-esp-elf-g++: error:
CreateProcess: No such file or directory`, mimo że wygenerowany kod i toolchain
są poprawne. Skrypt `scripts/verify-junexis-02.ps1` ustawia dla embedded gate
`TEMP/TMP=C:\tmp` oraz `PLATFORMIO_CORE_DIR=C:\tmp\pio-core`.

### Przykłady `grammar/`

- **14/14** przechodzi `CLI parse`,
- `07-expressions.jnx` potwierdza składnię aliasów `or`/`||`, `and`/`&&`,
  `not`/`!`,
- `14-platform-annotations.jnx` jest traktowany jako poprawny korpus składniowy;
  jego celem nie jest dowód semantyki platformy ani codegenu.

## Rola przykładów

Przykłady powinny pełnić różne, jawne role. Nie każdy plik musi prezentować
cały język. Główne przykłady podlegają kontraktowi uruchomieniowemu, a
`grammar/` kontraktowi poprawności składniowej.

| Przykład | Rola robocza |
|----------|--------------|
| `13-minimal-pipeline.jnx` | Najmniejszy smoke test parsera, IR i podstawowego codegenu. |
| `01-temperature-monitor.jnx` | Główny kandydat na pionowy scenariusz `@embedded → @server → @report`. |
| `02-ecommerce-api.jnx` | Scenariusz serwerowy dla `queue`, protokołu REST i wielu usług. |
| `03-smart-home.jnx` | Scenariusz złożonej topologii, wielu urządzeń i Narrative. |
| `04`–`12` | Przykłady tematyczne i regresyjne; ich dokładne role wymagają klasyfikacji. |
| `grammar/` | Pozytywny korpus poprawnej składni; wymaga parsera/AST, ale nie codegenu ani wykonania. |

### Przykłady projektowe

Zgodnie z D18 docelowy workflow Junexis jest project-first. Płaskie pliki
`examples/*.jnx` pozostają corpusowym materiałem regresyjnym, a nowe przykłady
reprezentujące realne systemy powinny powstawać jako katalogi projektu.

Pierwszy taki przykład:

```text
examples/projects/01-temperature-monitor/
  junexis.project.toml
  src/main.jnx
  user/embedded/platformio.ini
```

`user/embedded/platformio.ini` pokazuje project overlay: developer ma prawo
zmieniać konfigurację PlatformIO w wersjonowanym pliku poza `out/`, a build
kopiuje ją do `out/embedded/platformio.ini`.

Ten sam mechanizm obejmuje `user/server/go.mod` oraz
`user/embedded/CMakeLists.txt`, gdy projekt potrzebuje przejąć kontrolę nad
konfiguracją Go albo CMake.

### Laboratorium inteligentnego domu

`examples/projects/10-smart-home-lab` jest pierwszym wykonywalnym fundamentem
Laboratorium Junexis. Deterministyczny scenariusz serwerowy przechodzi przez
normalny pomiar, pogorszenie jakości powietrza, utratę czujnika i odzyskanie
działania. Każdy krok emituje jawną decyzję z regułą oraz działaniem.

Zweryfikowano `check`, `analyze`, generowanie serwera, natywną kontrolę
artefaktu i uruchomienie pełnej sekwencji. Projekt ma panel, sterowane awarie,
punkty kontrolne, eksperymenty, odzyskiwanie działania i trwały stan procesu.
M19.7.1 potwierdza również budowanie przeniesionych źródeł dla całej macierzy
oraz jawny, deterministyczny tryb symulacji bez ukrytego powrotu do sprzętu.
Zadania okresowe oraz odbiorcy kanałów kończą się razem z serwerem.

Drugi przykład project-first pokazuje praktyczny `extern` dla kodu użytkownika:

```text
examples/projects/02-server-storage-extern/
  junexis.project.toml
  src/main.jnx
  user/server/storage.go
```

`02-server-storage-extern` generuje dla targetu `server` manifest:

```text
out/server/externs/junexis.externs.toml
```

Manifest opisuje kontrakt `TemperatureStore` i wskazuje, że `user/server/storage.go`
jest plikiem developera, którego generator nie powinien nadpisywać.

Kontrakt:

```powershell
dotnet run --project compiler/JunexisLang.Compiler.CLI -- check examples/projects/01-temperature-monitor
dotnet run --project compiler/JunexisLang.Compiler.CLI -- build examples/projects/01-temperature-monitor
```

Testy CLI sprawdzają, że projektowy przykład 01 używa manifestowego `entry`,
`targets` i generuje artefakty dla `server`, `embedded` oraz `report`.

## Audyt `01-temperature-monitor.jnx`

### Obecny model

Przykład zawiera:

- dwa kontrakty danych: `TempReading` i `Alert`,
- urządzenie `kitchen_sensor @embedded(esp32c3)`,
- task `TempSensor @embedded`,
- task `TempProcessor @server`,
- task `AlertDashboard @report`,
- kierunkowy pipeline `TempSensor → TempProcessor → AlertDashboard`,
- komunikację opartą wyłącznie na `chan<T>`,
- jawne granice `extern` dla sensora i renderera.

Nie zawiera:

- `deployment`,
- jawnej deklaracji `protocol`,
- `narrative`,
- `extern type`.

### Konstytucja języka

| Pojęcie | Stan w przykładzie | Obserwacja |
|---------|--------------------|------------|
| `struct` | obecne | Kontrakty danych są czytelne i używane na krawędziach. |
| `task` | obecne | Trzy odpowiedzialności odpowiadają trzem platformom. |
| `device` | obecne | Alias urządzenia jest rozwiązywany do profilu `esp32c3` i konfiguracji PlatformIO. |
| `pipeline` | obecne | Kierunek przepływu jest jawny; IR tworzy deskryptory mostów dla granic platform. |
| `deployment` | brak | Rozmieszczenie jest częściowo zaszyte w adnotacjach tasków. |
| `protocol` | brak | Komentarze obiecują MQTT/WebSocket, lecz deklaracja nie istnieje. |
| `narrative` | brak | Przepływ nie ma jawnego kontraktu obserwowalności. |
| `extern` | obecne | Operacje sprzętowa i UI mają typowane deklaracje; implementację wiąże host. |
| `chan/queue/stream` | częściowe | Przykład używa tylko `chan`, niezależnie od charakteru obu krawędzi. |

### Macierz implementacyjna

| Warstwa | Status | Dowód / ograniczenie |
|---------|--------|----------------------|
| Specyfikacja | `PARTIAL` | Konstrukcje są opisane, ale część gwarancji nie jest precyzyjna. |
| Gramatyka | `VERIFIED` dla tego pliku | Plik przechodzi parser. |
| AST | `VERIFIED` dla tego pliku | AST powstaje bez awarii. |
| Semantyka | `PARTIAL` | `check` jest czysty i sprawdza deklaracje, typy nazwane oraz arność wywołań; pełne type checking nadal nie istnieje. |
| IR | `PARTIAL` | Structural IR zachowuje deklaracje, a Distributed Topology IR loweruje REST, placement i transporty. |
| Codegen | `L2` | C++, Go i TypeScript przechodzą natywne toolchainy. |
| Runtime | `PARTIAL / L3 server` | Istnieje wykonywalny test zachowania tasku Go z wstrzykniętym `extern`; nie obejmuje całego przykładu. |
| E2E | `L4 wąski` | Powtarzalny Go → TypeScript dla `stream<Text>` i `junexis-wire/0.2`; nie obejmuje całego przykładu 01 ani embedded. |

### Aktualne diagnostyki

`CLI check` nie emituje dla tego pliku `E0020`, `E0021`, `W0022` ani `E0023`.
`Time`, `Duration`, `Symbol`, `now` i `epoch` należą do wbudowanego kontraktu,
a operacje platformowe są zadeklarowane jako `extern task`.

## Otwarte decyzje dla przykładu 01

### 1. Task, device i deployment

Obecny zapis:

```junexis
task TempSensor() @embedded(kitchen_sensor)
```

jest interpretowany przez walidator jako binding do deklaracji `device`. To
częściowo przenosi odpowiedzialność deploymentu do adnotacji tasku.

Trzeba ustalić jeden model:

- task wskazuje urządzenie bezpośrednio, a deployment tylko tworzy instancje,
- albo task deklaruje wyłącznie wymaganie `@embedded`, a deployment wiąże jego
  instancję z konkretnym `device`.

Drugi model lepiej zachowuje ustalony podział odpowiedzialności i pozwala użyć
tego samego tasku na wielu urządzeniach.

### 2. Rodzaj komunikacji

Charakter scenariusza sugeruje do rozważenia:

```text
TempSensor    → queue<TempReading> → TempProcessor
TempProcessor → stream<Alert>      → AlertDashboard
```

Pierwsza krawędź może wymagać trwałości podczas utraty połączenia urządzenia.
Druga reprezentuje obserwację aktualnych alertów przez UI. Zmiana nie powinna
zostać wykonana przed doprecyzowaniem ACK, storage budget i semantyki stream.

### 3. Granice `extern`

`dht_read_celsius` i `render_alert` są jawnymi, typowanymi `extern task`.
Go i TypeScript emitują wstrzykiwalne granice, a C++ dokładną sygnaturę
bootstrapową. Brak bindingu nie jest dowodem L3. `epoch` i `now` są built-inami
czasu.

### 4. Protokoły

Distributed Topology IR zapisuje rzeczywisty bootstrap: TCP/JSON na granicy
embedded–server oraz WebSocket/MessagePack dla `chan` server–report. Każda
krawędź trafia do `wire-contract.json`. Jest to jawnie odróżnione od docelowego
automatycznego MQTT opisanego w specyfikacji; MQTT nie ma jeszcze dowodu L4.

## Kryterium docelowe przykładu 01

Przykład można oznaczyć jako `VERIFIED`, gdy:

1. `check` kończy się bez fałszywych i niejawnie ignorowanych diagnostyk,
2. pipeline ma zweryfikowane typy oraz kierunki wszystkich krawędzi,
3. binding task–device–deployment ma jedną, udokumentowaną semantykę,
4. wybór protokołu jest jawny i zgodny z codegenem,
5. C++, Go i TypeScript budują się w kontrolowanym środowisku,
6. dane przechodzą przez oba mosty,
7. Narrative potrafi powiązać zdarzenia na całej ścieżce,
8. całość uruchamia jedno powtarzalne polecenie E2E.
