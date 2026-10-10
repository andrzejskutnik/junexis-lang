# CLI and Language Server baseline (M5)

Status: **0.2 Experimental — M5 baseline**
Data weryfikacji: **2026-06-24**

Ten dokument definiuje wąski, wykonywalny kontrakt Milestone 5. Nie ustanawia
stabilnego API narzędzi ani kompletnego modelu IDE dla całego projektowanego
języka.

## Publiczny kontrakt CLI

Podstawowymi komendami użytkownika są:

```text
junexis check <plik.jnx>
junexis build <plik.jnx> [-o <out-dir>] [-t <platforma>]
junexis check [katalog-projektu]
junexis build [katalog-projektu] [-o <out-dir>] [-t <platforma>]
junexis run [katalog-projektu] [--duration <sekundy>] [--go-exe <go>] [--npm-exe <npm>] [--node-exe <node>] [--no-open]
junexis report scaffold [katalog-projektu] [--declarative | --framework dom|react|vue] [--dry-run]
junexis package [katalog-projektu] [-o <bundle.zip>] [-t <platforma>] [--sign-key <pem> --signer <name>]
junexis lab package [katalog-projektu] [-o <laboratorium.junexis-lab.zip>] [--runtime win-x64|linux-x64|osx-arm64]
junexis lab verify <laboratorium.junexis-lab.zip> [--json]
junexis lab run <laboratorium.junexis-lab.zip> [--http-port <port>] [--health-port <port>] [--tcp-port <port>] [--duration <sekundy>] [--trace-url <url>]
junexis bundle sign <bundle.zip> --key <pem> --signer <name>
junexis bundle verify <bundle.zip> [--auth-policy permissive|signed|trusted] [--trusted-signer <sha256>]
junexis deploy preflight <bundle.zip> [-t <platforma>] [--auth-policy permissive|signed|trusted] [--json]
junexis deploy embedded probe [--platformio-exe <pio>] [--json]
junexis deploy embedded select --port <port> [--selection-out <path>] [--platformio-exe <pio>] [--json]
junexis deploy embedded flash <bundle.zip> --selection <selection.json> [--receipt <package-receipt.json>] [--platformio-exe <pio>] [--work-dir <dir>] [--receipt-out <path>] [--json]
junexis conformance <semantic-conformance.json> [--json]
```

`conformance` waliduje raport projektowy `junexis.semantic-conformance/0.1`
albo związany z wydaniem raport `junexis.semantic-conformance/0.2`. Pokazuje
status, liczbę scenariuszy oraz luki `achievedLevel → targetLevel`; dla 0.2
sprawdza także SHA-256 bindingu `Build ID + Bundle ID + ledger SHA-256` i
prezentuje `BOUND`. Poprawny raport `partial` kończy się kodem `0`: jest dowodem
jakości i źródłem opinii, nie błędem składni, semantyki ani codegenu.
Nieprawidłowy, zmanipulowany lub nieobsługiwany format kończy się kodem `2`.

`build` korzysta dokładnie z tego samego pipeline'u co historyczna nazwa
`compile`, która pozostaje aliasem kompatybilności prototypu. Sukces komendy
`build` oznacza utworzenie kontraktu artefaktu właściwego dla targetu. Nie jest
sam w sobie dowodem natywnego buildu; temu służy `artifact-gate` z M4.

`run` jest projektowym, bezpiecznym skrótem pierwszego uruchomienia. Wykonuje
`build` dla wykonywalnych targetów `server` i `report`. Projekt wyłącznie
serwerowy uruchamia wygenerowany program Go pod nadzorem ograniczonej czasowo
sesji CLI. Projekt `server + report` dodatkowo instaluje zależności
wygenerowanego artefaktu, buduje bundle przeglądarkowy, przydziela rozłączne
porty dla HTTP, health, wszystkich mostów TCP i panelu, a następnie czeka na
gotowość obu procesów. Adres panelu jest wypisywany i domyślnie otwierany w
przeglądarce; `--no-open` wyłącza wyłącznie ten ostatni krok.

Od 2026-09-25 CLI publikuje `JUNEXIS_READY {json}` ze schematem
`junexis.panel-ready/1`, UUID `sessionId`, rodzajem `kind` (`server`/`report`)
i lokalnym adresem `url`. Panel server-only pochodzi z `entrypoint` w istniejącym
`laboratory.json`; brak deklaracji nie tworzy adresu panelu. Report ma pierwszeństwo.
Gotowość wymaga odpowiedzi HTTP 2xx; logi potomnych procesów są prefiksowane.
Starsze `READY report · URL` pozostaje po nowym zdarzeniu.
Pełne reguły odbioru są utrzymywane poza repozytorium Junexis Lang.

Sesja trwa domyślnie osiem sekund, a jawne `--duration` przyjmuje zakres
`1–300`. `Ctrl+C`, koniec czasu lub błąd gotowości prowadzą do zamknięcia
wszystkich procesów należących do polecenia. Komenda nie tworzy System Bundle,
deployment receipt ani dowodu wdrożenia; do tych granic służą `package` i
`deploy`.

`report scaffold` tworzy raz plik autora `user/report/adapter.ts`. Domyślny
wariant `dom` nie wymaga biblioteki UI. Warianty `react` i `vue` tworzą również
`user/report/package.json` ze schematem `junexis.report-package/0.1`. CLI scala
z niego wyłącznie `dependencies` i `devDependencies`; odrzuca próby zmiany
skryptów, nazwy paczki oraz zależności należących do Junexis. Żaden z plików
autora nie jest nadpisywany.

Podczas builda adapter jest kopiowany do `out/report/user-adapter.ts`, a
manifest zależności do `out/report/user-package.json`. Oba mają w inventory
ownership `user-overlay`. Zależności są deterministycznie scalane z
generowanym `out/report/package.json`, po czym adapter kompiluje się razem z
kontraktem typów, transportem oraz lifecycle połączenia. Bez adaptera działa
bezpieczny renderer automatyczny.

Wariant `--declarative` nie tworzy kodu adaptera. Analizuje projekt, znajduje
rzeczywiste zadania `@report` i tworzy `user/report/view.json` ze schematem
`junexis.report-view/0.1`. Build sprawdza nazwy zadań, unikalność paneli,
ograniczenia liczby pól, bezpieczne ścieżki zagnieżdżonych wartości oraz formaty
`auto`, `number`, `boolean`, `text` i `json`. Następnie kompiluje konfigurację
do typowanego `out/report/user-view.ts`; oryginał pozostaje w artefakcie jako
`user-overlay`. Zadania nieopisane w widoku nadal korzystają z renderera
automatycznego.

Targety są rozstrzygane w sposób fail-closed. W trybie projektu manifest lub
jawne `--target` tworzą listę `requestedTargets`; każdy z nich musi mieć co
najmniej jedno odpowiadające zadanie w analizowanym programie. Brak zgodności
kończy się `B0003`. W trybie pojedynczego pliku bez `--target` CLI wyprowadza
targety wyłącznie z adnotacji `@embedded`, `@server` i `@report`. Pomocniczy
generator `proto` nie jest publicznym targetem wykonawczym. Same deklaracje
`extern task`, `extern type` oraz kontrakty `extern` opisują granicę integracji,
ale nie ustanawiają wykonywalnego punktu wejścia i nie czynią targetu
zastosowalnym. Jeżeli zakwalifikowany backend nie utworzy żadnego artefaktu
wykonawczego, build kończy się kontrolowaną diagnostyką `G0001` przed
publikacją manifestów.

JSON output schema 2.0 rozróżnia `requestedTargets`, `applicableTargets` i
`builtTargets`; historyczne pole `targets` oznacza targety efektywne, a dla
udanego builda jest równe `builtTargets`. `targetSelectionSource` wskazuje
`explicit`, `manifest` albo `inferred`. Te pola zachowują swoje rzeczywiste
wartości również w odpowiedzi błędnej; brak zakończonej analizy lub generowania
jest reprezentowany pustą listą, a nie domyślną wartością udającą wynik.

Eksperymentalny baseline `M7.7` dla `embedded` pozostaje osobnym, jawnym flow.
`deploy embedded probe` jest read-only i tylko enumeruje kandydatów urządzeń.
`deploy embedded select` zapisuje deterministic selection contract dla jednej,
wyraźnie nazwanej tożsamości urządzenia. `deploy embedded flash` wymaga takiego
selection contract, ponownie weryfikuje verified System Bundle i package receipt,
ponownie sprawdza obecność tego samego urządzenia w aktualnym probe i dopiero
potem uruchamia PlatformIO upload, zapisując deterministic flash receipt.

Każdy udany build publikuje w inventory także wersjonowany
`debug/junexis.source-map.json`. Kontrakt 1.0 wiąże stable task symbol ID,
stabilną ścieżkę statementu IR i pozycję `.jnx` z targetem, plikiem oraz linią
wygenerowaną w C++/Go/TypeScript. Wpisy `kind = task` zachowują zakres całej
funkcji, a `kind = statement` wskazują dokładną instrukcję. Backend zapisuje
neutralny komentarz `junexis:source`, dzięki któremu mapa pozostaje zgodna z
rzeczywistym tekstem artefaktu po zakończeniu emisji.

Build publikuje również `analysis/junexis.analysis.json`. Kontrakt 1.1 zachowuje
symbole, komunikacje i topologię z wersji 1.0 oraz dodaje `semanticRelations`:
task → pipeline, Narrative → task/pipeline, task → Narrative, task → supervisor,
task → circuit breaker i task → platform. Właściwości symboli przenoszą
deterministyczne metadane potrzebne Mentorowi, m.in. compliance, trace oraz
ownership granic extern. Konsumenci akceptują kompatybilne wersje `1.x`.

Po zapisaniu wszystkich artefaktów każdy udany `build` publikuje atomowo
kontrakt `latest-build` 1.0. Jest to trwały, jawny punkt synchronizacji dla
Studio, rozszerzenia VS Code, Mentora i debuggera:

- project mode: `<project-root>/.junexis/state/latest-build.json`,
- file mode: `<katalog-pliku>/.junexis/state/<nazwa-pliku>/latest-build.json`.

Receipt zawiera `schemaVersion`, 64-znakowy `buildId`, `mode`, bezwzględne
`projectRoot`, `entryFile` i `outputRoot`, listę `targets` oraz bezwzględne
ścieżki `bundleManifest`, `artifactManifest`, `debugManifest` i
`analysisSnapshot`. Ścieżki plików kontraktu muszą pozostawać wewnątrz
`outputRoot`, a entrypoint wewnątrz `projectRoot`. Konsument odrzuca nieznaną
wersję główną lub naruszenie tych zasad zamiast zgadywać.

Receipt powstaje jako plik tymczasowy w katalogu docelowym i staje się widoczny
dopiero przez atomowe zastąpienie. Nieudany parse, analiza, codegen albo
niezgodność preflightu nie zmienia poprzedniego receiptu. Jeśli publikacja
receiptu nie powiedzie się, build kończy się błędem `B0002`, nawet gdy część
artefaktów została już zapisana. JSON output schema 2.0 wskazuje opublikowany
plik w polu `latestBuildReceipt`; dla nieudanego builda pole ma wartość `null`.
Komenda `analyze` nie publikuje receiptu, ponieważ nie jest ukończonym buildem.

Jedna ścieżka statementu może wystąpić w mapie wielokrotnie, gdy lowering
tworzy kilka wykonywalnych operacji (np. trace komunikacji i właściwy send).
Każda lokalizacja przechowuje osobną linię oraz pierwszą wykonywalną kolumnę
pliku wynikowego. Debugger powinien wysłać wszystkie lokalizacje odpowiadające
żądanemu breakpointowi `.jnx` i zachować wynik weryfikacji każdej z nich.

Eksperymentalny model katalogu projektu, manifest `junexis.project.toml` i
izolowane `out/` per projekt opisuje [`14-project-layout.md`](14-project-layout.md).
Publiczna komenda `junexis restore [project]` należy do Installera i odtwarza
wszystkie zależności projektu. Deleguje pakiety aplikacyjne do prywatnego
`junexis-compiler restore`, który używa lokalnego indeksu albo zweryfikowanego
snapshotu HTTPS wskazanego przez `--registry`, projektowego cache oraz zapisuje
`junexis.packages.lock`; następnie odtwarza wymaganych dostawców do profilu
użytkownika z tego samego Registry albo z lokalnego zestawu wydania. Wariant
`--offline` ogranicza pakiety aplikacyjne do lockfile i cache, dlatego nie łączy
się z `--registry`. `--local-packages` i `--registry` są wzajemnie wykluczające.
`check` i `build` wymagają wcześniejszego restore dla `packages`, a
zweryfikowane eksporty włączają do wspólnego katalogu modułów.
Sieciowy klient pakietów stosuje politykę M17.8.1: ograniczone ponowienia tylko
dla bezpiecznego `GET`, `Retry-After`, osobne budżety próby i operacji oraz
ręczną kontrolę przekierowań. Przed ponowieniem przerwanego artefaktu cofa plik
stagingowy; błąd, timeout albo anulowanie nie zmieniają ostatniej poprawnej
pamięci podręcznej ani `junexis.packages.lock`. Szczegóły i macierz awarii
zawiera [`91-registry-client-resilience.md`](91-registry-client-resilience.md).
Jeśli projekt ma katalog `migrations/`, `build` kopiuje jego pliki `.sql` do
`out/server/migrations/` jako developer-owned snapshot. Dla programu z
`extern storage` generator dodaje kontrakt `junexis.storage/0.1` i neutralnego
klienta Runtime, bez kodu providera i bez sterownika bazy. Sam `check` ani `build` nie
otwiera bazy i nie wykonuje migracji; dzieje się to dopiero podczas `Open` w
uruchomionej aplikacji.
Jeżeli manifest zawiera `providerPackages` i sekcje `[storage.*]`, discovery
waliduje lokalne pakiety jeszcze przed kompilacją. Build wiąże je z osiągalnymi
deklaracjami storage i publikuje `out/debug/junexis.providers.lock.json` w
schemacie `junexis.providers.lock/0.1`. `B0006` oznacza binding do nieistniejącej
deklaracji, a `B0007` deklarację pozostawioną bez bindingu. Build nie uruchamia
kodu providera; wykonuje go dopiero zweryfikowany launcher.

Od M12.8 `junexis run --storage-runtime-host <plik>` wykonuje storage binding
po weryfikacji lockfile i ponownym obliczeniu hashy pakietu. `env:` i `secret:`
są rozwiązywane dopiero przed startem i nie trafiają do outputu. `junexis
package` przyjmuje tę samą opcję oraz włącza host, provider i manifest
`junexis.storage.deployment/0.1` do System Bundle.
CLI nadal zachowuje tryb pojedynczego pliku dla pracy kompatybilnościowej, ale
komendy `check`, `build`, `run`, `deploy` i `artifact-gate` potrafią odkryć projekt z
bieżącego katalogu albo z katalogu podanego jako wejście. Operacje dostarczenia
całego systemu — `bundle create` i `package` — wymagają manifestu projektu.
`package` wykonuje analysis preflight, natywne bramki targetów, build związany
z Build ID preflightu oraz utworzenie i weryfikację System Bundle. Dokładny
kontrakt opisuje [`17-system-package.md`](17-system-package.md).
`lab package` rozszerza tę ścieżkę dla projektu laboratorium: wymaga
`laboratory.json` i `README.md`, buduje samodzielny serwer dla bieżącego systemu
albo jawnego `--runtime` z macierzy `win-x64`, `linux-x64`, `osx-arm64` oraz
pakuje go razem ze zweryfikowanym System Bundle. `lab verify` działa offline.
`lab run` sprawdza paczkę przed rozpakowaniem, odrzuca środowisko inne niż
bieżący system i uruchamia zgodną paczkę bez checkoutu źródeł oraz bez lokalnej
kompilacji. Kontrakt opisuje
[`22-laboratory-package.md`](22-laboratory-package.md).
`deploy preflight` przyjmuje wynik tego przepływu, wymaga zgodnego package
receipt i raportuje techniczną gotowość oddzielnie od opinii Mentora. Kontrakt
opisuje [`18-deployment-preflight.md`](18-deployment-preflight.md).
Podpis odłączony, jawne zaufanie i politykę dopuszczenia opisuje
[`20-system-bundle-authenticity.md`](20-system-bundle-authenticity.md).

W trybie pojedynczego pliku `build <plik.jnx>` bez jawnego `--out` nie zapisuje
już do wspólnego `out/` obok przykładów. Domyślny wynik trafia do
`<katalog-pliku>/.junexis/out/<nazwa-pliku>/`, co utrzymuje tryb plikowy jako
compatibility/playground bez mieszania artefaktów wielu systemów.

`junexis clean [project]` usuwa wygenerowany stan projektu:

- `outDir` z manifestu,
- `.junexis/build/`,
- `.junexis/diagnostics/`.
- `.junexis/state/`.

Nie usuwa `src/`, `junexis.project.toml`, `junexis.packages.lock` ani
`.junexis/cache/`. Opcja
`--dry-run` wypisuje ścieżki bez kasowania.

`junexis doctor [path]` jest nieinwazyjną diagnostyką orientacyjną. Sprawdza,
czy CLI widzi projekt, manifest, entrypoint, targety, ich zgodność z adnotacjami
platformowymi źródła, `outDir`, stan
wygenerowanych katalogów oraz wynik `check` dla entrypointu. Dla pojedynczego
pliku `.jnx` wyraźnie oznacza tryb file-mode compatibility i pokazuje
izolowaną ścieżkę `.junexis/out/<plik>`.

## Jeden pipeline diagnostyczny

Po poprawnym parsowaniu i walidacji semantycznej CLI oraz LSP wykonują wspólny
etap:

```text
AST → Structural IR → logical topology → distributed topology lowering
```

Oznacza to, że otwarty dokument publikuje także diagnostyki `R*`, `T*` i `D*`,
a nie tylko wcześniejsze diagnostyki parsera i semantyki. Interfejs użytkownika
nie może definiować osobnej, uboższej wersji poprawności programu.

## Kontrakt LSP

Baseline serwera stdio obejmuje:

| Metoda | Gwarantowany zakres 0.2 |
|--------|------------------------|
| `textDocument/didOpen`, `didChange`, `didClose` | pełna synchronizacja tekstu i publikacja diagnostyk kompilatora |
| `textDocument/hover` | opis symboli rozpoznanych w bieżącym dokumencie |
| `textDocument/completion` | słowa kluczowe, typy wbudowane, symbole dokumentu i podstawowe pola |
| `textDocument/definition` | nawigacja do deklaracji w bieżącym dokumencie |
| `textDocument/references` | tekstowe referencje symbolu w otwartych dokumentach |
| `textDocument/rename` | workspace edit dla poprawnego identyfikatora w otwartych dokumentach |
| `textDocument/codeAction` | narrative rewrite `&&` → `and` i `||` → `or` poza tekstami i komentarzami |
| `textDocument/documentSymbol` | deklaracje najwyższego poziomu dla Outline |
| `textDocument/semanticTokens` | odporna na niepełny kod klasyfikacja leksykalno-semantyczna |

Semantic tokens celowo działają także przy chwilowo niepoprawnym AST. Obejmują
komentarze, słowa kluczowe, literały, operatory, typy, nazwy deklaracji,
adnotacje i identyfikatory. Nie są jeszcze dowodem pełnego typowania każdego
identyfikatora.

### Workspace IDE dla named query storage (M13.13)

Dla lokalnego albo importowanego modelu `storage` serwer językowy wyprowadza
funkcję `Model_Query` i typ `Model_QueryRow` z tego samego zakresu modułu co
semantyka. Completion respektuje selective import i lokalny alias. Hover
pokazuje typowany kontrakt, końcowy shape wiersza oraz kanoniczną nazwę Runtime,
a Go to Definition prowadzi do źródłowej deklaracji `query`, również gdy jej
plik nie został otwarty. Outline pokazuje model, encje i query.

Import całego modułu przez alias nie ustanawia jeszcze kwalifikowanej składni
wywołań query i otrzymuje ostrzeżenie `SQC006`. Pełny kontrakt i bramka odbioru
znajdują się w
[`54-storage-query-developer-experience.md`](54-storage-query-developer-experience.md).

## Poziomy dowodu M5

1. **C0 — build:** solution oraz VS Code extension kompilują się.
2. **C1 — component contract:** każdy handler ma test zachowania na otwartym
   dokumencie, a `build` ma test uruchamiający rzeczywisty proces CLI.
3. **C2 — protocol contract:** test uruchamia osobny proces LSP, negocjuje
   capabilities, wysyła JSON-RPC przez stdio i wykonuje wszystkie metody M5.
4. **C3 — extension artifact:** `junexis-tooling/vscode-extension` preferuje
   LspHost, używa `junexis build` dla artefaktów i tworzy pakiet VSIX.

Baseline M5 osiąga C3. Ręczne zachowanie konkretnej wersji VS Code, publikacja
w marketplace oraz instalacja globalnych paczek pozostają osobnymi zadaniami
wydaniowymi.

## Świadome granice

Poza ogólnym baseline pozostają między innymi:

- pełny indeks i nawigacja między wieloma plikami oraz importami poza
  zweryfikowanymi symbolami projektu M11.5 i named query storage M13.13,
- pełna analiza zakresów lokalnych i precyzyjne rozstrzyganie przesłonięć,
- semantyczne rozstrzyganie zakresów dla rename/references, signature help
  oraz formatting przez LSP,
- incremental parsing i diagnostyka zależności całego workspace,
- test extension host uruchamiany wewnątrz konkretnej dystrybucji VS Code.

Te ograniczenia nie cofają M5, ale nie wolno przedstawiać baseline jako
pełnoprawnej, stabilnej obsługi IDE całego języka.
