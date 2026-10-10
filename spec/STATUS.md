# Junexis Lang — Status specyfikacji

> **Aktualna wersja:** 0.2.0  
> **Poziom dojrzałości:** EXPERIMENTAL  
> **Data aktualizacji statusu:** 2026-08-25

## Znaczenie statusu

Junexis Lang 0.2 jest eksperymentalną specyfikacją języka i nierównomiernie
zaimplementowanym prototypem toolchaina. Składnia, semantyka, diagnostyki,
formaty IR, generowany kod i kontrakty runtime mogą zmieniać się bez zachowania
kompatybilności wstecznej.

Dokumentacja opisuje projektowany język. Nie jest sama w sobie dowodem, że dana
funkcja działa. Dowodem jest dopiero powtarzalna ścieżka implementacyjna:

```text
specyfikacja → gramatyka → AST → semantyka → IR → codegen → runtime → E2E
```

Aktualnie zamknięte kontrakty infrastrukturalne są węższe niż cały powyższy
łańcuch: M1 gwarantuje parser + AST, a M2 bezstratny Structural IR dla 27 plików
obu korpusów. M3 gwarantuje logiczną topologię dla 13 głównych przykładów. M4
ma poziomy dowodu L0–L4 opisane w `11-code-generation.md`: wszystkie główne
przykłady osiągają L1, a wszystkie właściwe artefakty bootstrapowe osiągają L2
(embedded 10/10, server 12/12, report 4/4). Backend Go ma wąski wykonywalny test
L3 dla zachowania tasku i wstrzykniętego `extern`. M4d posiada baseline
Distributed Topology IR dla bindingów REST, placementu i transportów oraz
powtarzalny test L4: uruchomiony wygenerowany Go przekazuje wersjonowaną kopertę
`junexis-wire/0.2` do skompilowanego wygenerowanego klienta TypeScript.
M5 posiada baseline C3 opisany w `12-cli-and-lsp.md`: procesowy kontrakt
`junexis build`, wspólną diagnostykę CLI/LSP, pełną testową sesję JSON-RPC przez
stdio oraz pakowalny artefakt VS Code extension. Nie oznacza to jeszcze
kompletnego Typed Semantic IR, pełnej macierzy L3/L4 ani workspace-wide IDE.
M5.5 posiada wąski baseline R4 opisany w `13-compiler-runtime-baseline.md`:
kompilator/CLI/LSP przechodzą 423 testy, runtime .NET 540/540, embedded C 2/2,
a aktywne bootstrapowe runtime'y C++, Go i TypeScript mają wykonywalną macierz
`chan`/`queue`/`stream` oraz pionowy test Go → TypeScript przez
`junexis-wire/0.2`.

## Poziomy funkcji

| Status | Znaczenie |
|--------|-----------|
| `CORE` | Element należy do projektowanego rdzenia 0.2, ale nadal może się zmienić. |
| `EXPERIMENTAL` | Kierunek jest badany; składnia lub semantyka nie są ustalone. |
| `SYNTAX-ONLY` | Konstrukcja jest parsowana, lecz nie ma pełnej semantyki albo wykonania. |
| `PARTIAL` | Istnieje część ścieżki implementacyjnej, ale nie pełne E2E. |
| `UNVERIFIED` | Istnieje deklaracja lub implementacja, lecz nie ma aktualnego, powtarzalnego dowodu jej działania. |
| `VERIFIED` | Konkretna ścieżka i zakres mają powtarzalny test E2E. Nie oznacza to stabilności całego języka. |
| `RESERVED` | Nazwa lub token są zarezerwowane, lecz funkcja nie należy jeszcze do języka wykonywalnego. |
| `ABSENT` | Funkcja nie istnieje w bieżącej wersji. |

W specyfikacji 0.2 etykieta `CORE` zastępuje wcześniejsze `STABLE`. Oznacza
ważność dla tożsamości języka, a nie gwarancję kompatybilności czy kompletność
implementacji.

Numer 0.2 jest świadomym rebaseliningiem dojrzałości specyfikacji, a nie
kontynuacją dawnych, zbyt optymistycznych numerów prototypu. Nie ustanawia
jeszcze tagu wydania ani wersji pakietów. Historyczne oznaczenia pozostają
zapisem rozwoju prototypu, lecz nie określają aktualnego poziomu języka.

## Projektowany rdzeń 0.2

Do rdzenia należą pojęcia określone przez filozofię języka:

- `struct` i kontrakty danych,
- `task` i zachowanie,
- `device` i charakterystyka sprzętu,
- `pipeline` i kierunki logicznego przepływu,
- `deployment` i rozmieszczenie instancji,
- `protocol` i przekraczanie granic platform,
- `narrative` i wyjaśnialność przebiegu,
- `extern task` oraz `extern type` jako kontrolowana granica integracji,
- `chan<T>`, `queue<T>` i `stream<T>` jako różne rodzaje relacji komunikacyjnych,
- platformy `@embedded`, `@server` i `@report`.

Obecność elementu w rdzeniu nie oznacza, że jest już kompletnie zaimplementowany.
Zakres wspieranych backendów, urządzeń i protokołów może być początkowo wąski.

## Kandydat do rozszerzenia rdzenia: zasoby i I/O (D17)

`resource` został przyjęty jako brakująca ogólna odpowiedzialność modelu
języka: opisuje, z jakiego stanu albo jakiej zdolności I/O korzysta task. Ogólna
deklaracja `resource` nadal ma status `ABSENT`. Relacyjny podzbiór `storage` ma
od M13.2 status `EXPERIMENTAL` i działającą ścieżkę parser → AST → Structural
IR → model/query-plan → Runtime invocation. Od M13.5 providery memory i SQLite
wykonują zgodny pion read-only. M13.6 dodaje bezpieczny podzbiór SQL pushdown
SQLite. M13.7 dodaje zweryfikowane live wykonanie PostgreSQL, a M13.8 jego
bezpieczny SQL pushdown. M13.9 generuje typowany klient named query dla
bootstrapowego targetu Go i potwierdza pełny pion przez rzeczywisty Host oraz
provider memory. M13.10 wiąże named query bezpośrednio z ciałem tasku `@server`,
syntetyzuje typ wyniku i zarządza lifecycle providera. M13.11 pozwala zachować
model `storage` w osobnym module projektu i importować jego pochodne API query
do tasku aplikacyjnego. M13.12 dodaje lokalny alias tego API, obniżany do
kanonicznej nazwy modelu przed wspólnym IR. M13.13 udostępnia query i ich
syntetyczne typy w workspace IDE: completion, hover, Go to Definition oraz
Outline korzystają z tego samego zakresu importów co kompilator. Klienci innych
targetów pozostają otwarci.

M14.1 rozpoczyna ścieżkę zapisu od osobnego Mutation Plan IR, M14.2 dodaje
składnię `.jnx` oraz projekcję źródłową, a M14.3 obniża poprawny plan do
neutralnej operacji Storage Runtime `mutation.plan` i definiuje jawny wynik
zapisu. M14.4 dodaje kontrolę Host oraz pierwsze atomowe wykonanie przez
provider memory, a M14.5 potwierdza tę samą semantykę i trwałość w SQLite.
M14.6 zapisuje w SQLite wyłącznie dowiedzioną deltę parametryzowanych wierszy,
zachowując wspólny kernel i bezpieczny fallback. M14.7 rozszerza bazowe,
atomowe wykonanie tej samej semantyki na PostgreSQL i potwierdza je live na
wersji 17.5. M14.8 wspólnie dowodzi deltę dla SQLite i PostgreSQL oraz zapisuje
w PostgreSQL wyłącznie zmienione wiersze.

Rozproszony `stream<T>` pozostaje typem relacji komunikacyjnej. Sekwencyjne I/O
(plik, pamięć, ZIP/GZIP, socket) będzie projektowane oddzielnie jako typowane
źródła, ujścia i transformacje. Aktualny kierunek opisuje
[`10-resources-and-io.md`](10-resources-and-io.md); nie ustanawia on jeszcze
kanonicznej składni.

## Readiness 0.2 przed M6

Stan 0.2 po M5.5 jest wystarczający, aby rozpocząć M6 jako etap narzędziowy
(`Narrative`, debug/observability, embedded toolchain), ale nie jako etap
rozszerzania języka bez kontroli. M6 powinien korzystać z aktualnego rdzenia,
a nie zakładać ukrytej stabilności elementów częściowych.

| Obszar | Readiness przed M6 | Uwagi |
|--------|--------------------|-------|
| Parser, AST i Structural IR | `VERIFIED` | M1/M2 dla 27 plików obu korpusów. |
| Topologia logiczna | `VERIFIED` w baseline | M3 dla 13 głównych przykładów; pełny model sieci nadal ewoluuje. |
| Codegen artefaktów | `PARTIAL` + wąskie `VERIFIED` | L1/L2 szerokie, L3/L4 tylko reprezentatywne ścieżki; source-map 1.0 mapuje taski i zagnieżdżone statementy IR na dokładne linie C++/Go/TypeScript. |
| CLI i diagnostyki | `VERIFIED` w baseline | `parse/check/build`, JSON envelope i spójność z LSP. |
| LSP/IDE | `VERIFIED` w testowym baseline, `PARTIAL` produktowo | Stdio JSON-RPC i VSIX są pokryte; workspace-wide IDE pozostaje później. |
| Runtime .NET | `VERIFIED` jako reference runtime | 540/540 testów; NativeAOT niezweryfikowany z powodu brakującego toolchainu linkera. |
| Runtime bootstrap C++/Go/TypeScript | `VERIFIED` dla macierzy M5.5 | Sprawdzone `chan`, `queue`, `stream` i pionowy wire contract; brak jednego wspólnego ABI. |
| Embedded C ABI | `VERIFIED` lokalnie | CMake/CTest 2/2; nie jest jeszcze linkowany przez C++ codegen. |
| Embedded Rust ABI | `EXPERIMENTAL` | Brak w aktywnej bramce; środowisko audytu nie miało Cargo. |
| Benchmarki runtime | `PARTIAL` jako workflow | Jest runner i dokumentowana macierz w `junexis-runtime`; wyniki nie są jeszcze bramką CI. |
| Zasoby, bazy danych, pliki, sekwencyjne I/O | `PARTIAL`/`EXPERIMENTAL` | `storage` M13.2–M13.13 ma pełny pion read-only i workspace IDE. M14.1–M14.8 prowadzą zapis przez Mutation Plan, składnię, lowering, zgodne wykonanie memory + SQLite + PostgreSQL oraz wspólny bezpieczny delta pushdown obu trwałych providerów. Bezpośredni predicate pushdown pozostaje otwarty. Ogólny `resource` i typowane I/O pozostają absent. |
| Project layout i manifest | `EXPERIMENTAL`/`PARTIAL`; `VERIFIED` dla pionu pakietowego 0.1 | D18: workflow project-first. `junexis init` tworzy bezpieczny starter `server`; `build` rozróżnia requested/applicable/built targets, wymaga zwykłego wykonywalnego `task` i odrzuca rozbieżność `B0003`; same deklaracje `extern` nie kwalifikują targetu. `doctor` sprawdza zgodność manifestu ze źródłem. Dostępne są też `clean`, opcjonalny tryb projektu dla `check/build/deploy/artifact-gate`, pierwszy przykład `examples/projects/01-temperature-monitor`, project-first gate, izolowany file-mode output w `.junexis/out/<plik>` oraz atomowy kontrakt `latest-build` 1.0 w `.junexis/state/`. Pion pakietowy sprawdza lokalny indeks albo snapshot Registry, dokładne wersje, SHA-256, pamięć podręczną, `junexis.packages.lock`, import przez wspólny katalog modułów i ponowne restore/build bez sieci; publiczny wybór Registry zapewnia `restore --registry`. M17.8.2 dodaje ograniczone ponowienia, `Retry-After` oraz bezpieczne cofanie stagingu po przerwanym transferze. Podpisy i zakresy wersji pozostają otwarte. Tryb pojedynczego pliku zostaje jako compatibility/playground/regression corpus. |
| `narrative` | `CORE` filozoficznie, `PARTIAL` jako pełny subsystem; `VERIFIED` dla M7.10 | C++, Go i TypeScript współdzielą `junexis.tracecollector.v1`, globalną tożsamość IR i punkty task/`send`. M6.3–M6.16 budują wykonanie, Story, Studio Why, dowody, import, podpis, review i jawne propozycje Mentora. M7.8 naprawia kanoniczne `build_id` i `bundle_id`, a M7.10 dowodzi jednej ścieżki od zaufanego pakietu przez pięć zdarzeń runtime do Why i Diagnostic Story 1.2. Pełny debugger krokowy oraz fizyczny wielowęzłowy test ESP32 pozostają otwarte. |
| System delivery | `EXPERIMENTAL`; `VERIFIED` dla M7.10 | M7.1–M7.6 ustanawiają zweryfikowany Bundle, package/preflight, local deployment, Studio flow i owner-aware lifecycle. M7.7 dodaje bezpieczny probe/select/flash baseline, M7.8 zachowuje dokładną parę Build ID / Bundle ID, M7.9 rozdziela integralność i zaufanie, a M7.10 spina je w jeden raportowany golden flow `project → package → trusted preflight → deploy → observe`. PKI/KMS, revocation i fizyczny ESP32 smoke pozostają poza tym kontraktem. |
| Laboratory package | `EXPERIMENTAL`; paczki dla trzech platform, natywna próba `win-x64` | `junexis lab package/verify/run` tworzy paczkę z samodzielnym serwerem i System Bundle, sprawdza dokładne pokrycie SHA-256 i uruchamia interfejs WWW konkretnego demonstratora bez checkoutu źródeł lub kompilacji. Nie ustanawia osobnego komponentu „panel Laboratorium”. Pakiety `linux-x64` i `osx-arm64` przechodzą kompilację krzyżową oraz kontrolę kontraktu; ich natywne próby pozostają otwarte. Podpisane publiczne wydanie także pozostaje poza tym etapem. |
| Laboratory learning path | `VERIFIED` dla pierwszych pionów trzech etapów | Inteligentny dom realizuje `Poznaj`, konfigurowalne `Eksperymentuj` oraz pierwszy pion `Przejmij kontrolę`: efektywną konfigurację, automatyczne i zatwierdzane recovery, limit dowodów oraz bezpieczny reset. Pełny zakres polityk przeciążenia, aktualizacji i wielowęzłowego recovery pozostaje otwarty. |
| Laboratory equivalence | `EXPERIMENTAL`; natywna próba `win-x64` | Build ID jest niezależny od ścieżki i CRLF/LF. Trzy paczki zachowują jeden Build ID, Bundle ID i odcisk topologii. Pełny scenariusz Windows tworzy kanoniczny odcisk zachowania; natywne odciski Linux x64 i macOS ARM64 pozostają wymagane. |

Wniosek operacyjny: 0.2 jest zamknięte jako **eksperymentalny executable
baseline**, nie jako stabilny język produkcyjny. Kolejny etap powinien
koncentrować się na narzędziach, obserwowalności, UX błędów i reprodukowalnych
scenariuszach pionowych, a nie na dokładaniu dużych nowych konstrukcji składni.

## Zakończone kamienie milowe: M10 i M11

Po kontrakcie topologii M9 przyjęto dwa bliskie piony produktowe:

- **M10 — aplikacja TypeScript dla `@report`**: samodzielny panel
  przeglądarkowy, bezpieczny renderer automatyczny, jawny lifecycle połączenia,
  uruchamianie i pakowanie. Kontrakt znajduje się w
  [`23-report-application.md`](23-report-application.md).
- **M11 — moduły i SQLite**: pełny resolver projektów wieloplikowych, lokalne
  biblioteki, wspólna tablica symboli i pierwszy referencyjny provider storage
  dla SQLite. Kontrakt znajduje się w
  [`24-modules-and-sqlite.md`](24-modules-and-sqlite.md).

M10 ma status `VERIFIED`: generator tworzy bezpieczną aplikację
przeglądarkową, `junexis run` uruchamia razem serwer oraz panel, a `junexis
package` materializuje natywne binarium i statyczny panel. `deploy local`
uruchamia je bez Go, npm, TypeScriptu i bundlera. Framework-neutralny adapter
autora TypeScript/DOM, React i Vue ma scaffold, typowane hooki, kontrolowany
manifest zależności, bezpieczny fallback i jawne ownership. Walidowane widoki
deklaratywne mają działający pion projektowy.
Golden Home Report E2E jest zweryfikowany w prawdziwym Chrome: zwykła
obserwacja, widoczny reconnect, oznaczenie stale oraz `rejoined` po restarcie
serwera. Audyt M10.8 potwierdza kontrast WCAG AA obu motywów, klawiaturę,
widok 390 px, reduced motion oraz ograniczenie dużych danych. Studio nadzoruje
publiczne `junexis run`, publikuje URL, otwiera panel i mapuje diagnostykę
widoku oraz adaptera. Wszystkie kryteria M10 są opisane w
[`29-report-application-verification.md`](29-report-application-verification.md).
Składnia widoku w samym języku, czytnik ekranu, inne silniki przeglądarek i
kolejne paczki systemowe są rozszerzeniami po M10, a nie brakami odbioru.
M11 ma status `VERIFIED`:
M11.1 wprowadza aktywne `sourceRoots`, kanoniczną tożsamość modułu,
deterministyczny katalog źródeł oraz ochronę przed wyjściem poza projekt,
nakładaniem katalogów i nieprzenośnymi kolizjami nazw. Istniejąca składnia
`import` jest wykonywana przez M11.2 jako deterministyczny graf z diagnostyką
braku modułu, różnicy wielkości liter, cyklu i błędu parsera w pliku zależności.
M11.3 wiąże typy, funkcje, taski oraz deklaracje `extern` w osobnym zakresie
każdego modułu, z semantyką glob, alias i selective oraz diagnostyką brakującego
symbolu i kolizji. `extern storage` jest bootstrapową granicą ukończonego pionu,
a dowód wielomodułowy obejmuje `migracja → zapis → restart → odczyt → panel`.

M11.4 scala osiągalne moduły w jeden program wejściowy dla Structural IR,
topologii i codegenu. Importowany task zachowuje target i trafia do artefaktu,
natomiast pipeline oraz deployment pozostają własnością modułu wejściowego.
M11.5 rozszerza ten sam model na workspace LSP: indeksuje nieotwarte moduły,
przechodzi do definicji między plikami, obejmuje je references i rename oraz
usuwa fałszywe diagnostyki nieznanych symboli dla poprawnych importów.
M11.6 obsługuje namespacowane lokalne biblioteki z manifestu projektu i zapisuje
deterministyczny `junexis.modules.lock/0.1` z hashami wszystkich źródeł.
M11.7 zamraża niezależny od providera kontrakt `junexis.storage/0.1`:
lifecycle, efekty operacji, klasy błędów, timeout i anulowanie, transakcję oraz
gwarancje bezpieczeństwa. Semantic IR klasyfikuje operacje bootstrapowego
`extern storage`, a generator serwera publikuje maszynowy manifest zgodności.
Sam kontrakt M11.7 nie był jeszcze dowodem działania konkretnego providera.
M11.8 dostarcza referencyjny provider SQLite dla `@server`: generowany pakiet Go
bez CGO, parametryzowane operacje, lifecycle, health check, transakcje oraz
migracje chronione SHA-256. Test natywny potwierdza commit, rollback, ponowne otwarcie,
odczyt trwałego rekordu i odrzucenie zmienionej migracji. Wiązanie pełnego
systemu przez jawny adapter domenowy zostało zweryfikowane w M11.10.
M11.9 dodaje provider pamięciowy oraz wspólny interfejs domenowy `storage.Store`
dla memory i SQLite. Oba przechodzą ten sam natywny test operacji, transakcji,
rollbacku, anulowania i lifecycle. Nowa instancja memory pozostaje świadomie
pusta; trwałość, migracje i pełny restart procesu są dowodami SQLite/M11.10.
M11.10 domyka Golden Modular Storage E2E z pięciu modułów: REST zapisuje
obserwację przez domenowy `HomeStore`, rollback nie pozostawia skutku, drugi
proces odczytuje rekord z SQLite, a `@report` otrzymuje `HomeSnapshot` przez
zadanie serwera. `analyze` i bramka artefaktów komponują teraz ten sam graf
projektu co `check` oraz `build`. Integralny System Bundle 0.1 został
uruchomiony bez checkoutu: natywny serwer Windows x64 i aplikacja raportowa
osiągnęły gotowość, zapis SQLite przetrwał restart, a odpowiedź zachowała
rzeczywiste Build ID i Bundle ID. Bundle jest jeszcze niepodpisany i został
przyjęty przez jawną politykę permissive; podpisane wydanie pozostaje osobnym
etapem dystrybucji.

Po zamknięciu M11 kontrakt storage został rozszerzony o generowany provider
PostgreSQL opisany w
[`30-postgresql-provider.md`](30-postgresql-provider.md). Provider implementuje
ten sam `storage.Store`, parametryzowane operacje, pulę połączeń, migracje
chronione SHA-256 i klasyfikację SQLSTATE. Ma status `PARTIAL`: pakiet Go,
zależność pgx i mapowanie błędów są sprawdzane natywnie bez serwera, ale pełny
E2E migracja → zapis → rollback → restart → odczyt wymaga dostępnego endpointu
PostgreSQL i nie został jeszcze wykonany w bieżącym środowisku.

## Aktywny kierunek: M12 Independent Storage Runtime

Audyt po pierwszym providerze PostgreSQL ujawnił sprzężenie storage z
bootstrapowym generatorem Go. D21 rozdziela target, backend, deklarację storage,
provider, driver, Provider Runtime i binding. `@server` pozostaje placementem
tasku; nie jest właścicielem providera ani synonimem języka Go.

M12.1 ma status `ACCEPTED` jako kontrakt architektoniczny opisany w
[`31-independent-storage-runtime.md`](31-independent-storage-runtime.md).
Nie oznacza to jeszcze implementacji nowego runtime ani składni `storage`.
Wspólne ABI, host .NET i przeniesione providery przeszły bramkę M12.7;
generator nie emituje już dawnych pakietów Go. M12.2 ma status `VERIFIED` dla lokalnych pakietów projektu:
parser rozpoznaje jawne bindingi, resolver sprawdza `junexis.provider/0.1`,
wersję ABI, konfigurację i granice ścieżek, a build publikuje deterministyczny
`junexis.providers.lock/0.1` bez wartości sekretów. Binding do nieistniejącej
deklaracji lub brak bindingu zatrzymuje build kodem `B0006` albo `B0007`.
Kontrakt i granice opisuje
[`32-provider-package-and-binding.md`](32-provider-package-and-binding.md).
M12.3 ma status `VERIFIED BASELINE`: repo `junexis-runtime` zawiera neutralne
abstrakcje providera oraz niezależny host .NET dla
`junexis.storage.runtime/0.1`. Test uruchamia host jako osobny proces i
potwierdza handshake, lifecycle bindingu, operacje, wynik sekwencyjny,
transakcję, health, deadline, klasyfikację błędu oraz ochronę wartości sekretu.
Host ładuje jawnie wskazane assembly; spięcie launchera z hashem lockfile
pozostaje kolejnym pionem, a pierwszy rzeczywisty provider danych powstaje w
M12.4. Szczegóły hosta opisuje
[`33-storage-runtime-abi.md`](33-storage-runtime-abi.md).

M12.4 ma status `VERIFIED`: pakiet `memory@0.1.0` jest osobnym assembly w
repo `junexis-storage-providers`, deklaruje manifest `junexis.provider/0.1` i wykonuje
neutralne `put/get/list/delete`, health oraz transakcje przez host ABI 0.1.
Test procesu potwierdza dynamiczne załadowanie pakietu, a conformance obejmuje
deterministyczną sekwencję, commit, rollback, konflikt snapshotu, ulotny
reopen oraz bezpieczne limity pamięci. Provider jest używany przez neutralnego
klienta generatora i wspólną bramkę M12.7. Kontrakt opisuje
[`34-independent-memory-provider.md`](34-independent-memory-provider.md).

M12.5 ma status `VERIFIED`: pakiet `sqlite@0.1.0` wykonuje ten sam neutralny
kontrakt operacji przez Storage Runtime ABI, używa przypiętego
Microsoft.Data.Sqlite 10.0.11 i publikuje samodzielny pakiet Windows x64.
Dowód obejmuje persistence między procesami, prawdziwy kursor, commit,
rollback, migracje SHA-256, odrzucenie zmienionej historii oraz realną blokadę
SQLite sklasyfikowaną jako `busy`. Loader rozwiązuje managed i natywne
zależności z izolowanego pakietu. Generator Go korzysta z niego wyłącznie przez
neutralnego klienta Runtime. Szczegóły opisuje
[`35-independent-sqlite-provider.md`](35-independent-sqlite-provider.md).

M12.6 ma status `CONTRACT VERIFIED / POSTGRESQL LIVE E2E PENDING`. Wspólny
kontrakt `native-sql` udostępnia parametryzowane `sql.query`, `sql.scalar` i
`sql.command`, jawny shape kolumn oraz typy ABI niezależne od sterownika.
SQLite wykonuje realne query, scalar i command, weryfikuje typowany kursor i
odrzuca rozbieżny wynik. Niezależny pakiet `postgresql@0.1.0` używa Npgsql
10.0.3, ładuje się przez ten sam host, ma zamknięty manifest, walidację bindingu
i mapowanie SQLSTATE. Test live istnieje, lecz nie został wykonany, ponieważ w
bieżącym środowisku nie ma serwera ani skonfigurowanego sekretu połączenia.
Szczegóły opisuje
[`36-native-sql-and-postgresql.md`](36-native-sql-and-postgresql.md).

M12.7 ma status `VERIFIED`. Generator Go emituje neutralnego klienta
`junexis.storage.runtime/0.1`, a nie implementacje providerów ani zależności
driverów. Rebuild usuwa trzy wycofane pliki providerów z wcześniejszego outputu.
Golden Modular Storage E2E uruchamia ten klient przez osobny host i pakiet
SQLite; rzeczywista próba zapisu, rollbacku, restartu oraz odtworzenia danych
zakończyła się `PASS`. Repo providerów ma jeden przenośny zestaw conformance dla
memory, SQLite i PostgreSQL; dwa pierwsze przechodzą obowiązkowo, a live
PostgreSQL pozostaje zależny od `JUNEXIS_TEST_POSTGRESQL`. Szczegóły opisuje
[`37-generated-storage-runtime-client.md`](37-generated-storage-runtime-client.md).

M12.8 ma status `VERIFIED`. `junexis run` po bound buildzie ponownie hashuje
pakiet, porównuje kanoniczny `junexis.providers.lock/0.1`, rozwiązuje wartości
`literal/env/secret` bez logowania sekretów i uruchamia serwer z dokładnie
zweryfikowanym entrypointem. `junexis package` dołącza host, kompletny pakiet
providera oraz `junexis.storage.deployment/0.1` do inventory i integralności
System Bundle. Golden bundle z serwerem, panelem i SQLite przeszedł zapis,
restart i odtworzenie danych bez checkoutu oraz toolchainów. Szczegóły opisuje
[`38-storage-launcher-and-bundle.md`](38-storage-launcher-and-bundle.md).

M12.9 ma status `VERIFIED`. Golden Modular Storage prowadzi przez trzy
progresywne ścieżki: trwały zapis i restart w `Poznaj`, porównanie SQLite z
memory w `Eksperymentuj` oraz lockfile, sekrety i samodzielny System Bundle w
`Przejmij kontrolę`. Każdy etap zachowuje rytm hipoteza → wykonanie → dowód →
wyjaśnienie. Kontrakt opisuje
[`39-storage-laboratory.md`](39-storage-laboratory.md).

M12.10 ma status `VERIFIED WORKSPACE`. Implementacje `memory`, `sqlite` i
`postgresql`, ich testy oraz dokumentacja sterowników zostały przeniesione do
repo `junexis-storage-providers`. Repo `junexis-runtime` zachowuje Abstractions,
ABI, Host i testy hosta. Produkcyjny kod providerów zależy tylko od Abstractions;
testy procesowe korzystają dodatkowo z hosta. Osobna bramka providerów przeszła
23 testy, a dwie próby PostgreSQL zależne od serwera zostały jawnie pominięte.
Granica repozytoriów jest opisana w
[`40-provider-repository-boundary.md`](40-provider-repository-boundary.md).

M12.11 ma status `VERIFIED LOCAL FEED`. Runtime buduje wersjonowane paczki
`Junexis.Storage.Runtime.Abstractions` i `Junexis.Storage.Runtime.Host` 0.1.0
oraz manifest SDK z sumami SHA-256. Wszystkie projekty providerów korzystają z
`PackageReference`; w ich solution nie ma projektów ani referencji źródłowych z
repo Runtime. Bramka odtworzyła paczki przez izolowany lokalny feed i przeszła
23 testy, w tym uruchomienie Host z paczki w osobnym procesie; dwa testy live
PostgreSQL pozostały jawnie pominięte. Kontrakt i granicę dowodu opisuje
[`41-storage-runtime-sdk.md`](41-storage-runtime-sdk.md).

M13.1 ma status `VERIFIED COMPILER CONTRACT`. Kompilator zawiera logiczny model
danych `junexis.storage.model/0.1` oraz Query Plan IR
`junexis.storage.query-plan/0.1`. Pierwszy przenośny pion odczytu obejmuje
`scan`, `filter`, `project`, `sort` i `limit`, typowane parametry oraz walidację
modelu, typów i kształtu wyniku pomiędzy węzłami. Kontrakt nie zawiera SQL,
nazw tabel fizycznych, providera ani backendu targetu. Pięć testów kontraktu
przechodzi. M13.1 nie dodaje jeszcze składni języka ani wykonywania planu przez
provider. Zakres opisuje
[`42-storage-query-plan-ir.md`](42-storage-query-plan-ir.md).

M13.2 ma status `VERIFIED COMPILER CONTRACT`. Ustrukturyzowana deklaracja
`storage` opisuje encje, pola, nullability, klucz oraz nazwane zapytania
odczytowe. Parser, AST i Structural IR zachowują zapis autora, a projektor
tworzy kontrakty M13.1 dla `filter`, `project`, `sort` i `limit`. Parametry są
jawne jako `params.<nazwa>`, diagnostyki `SQPxxx` wskazują źródło, a nowe słowa
M13 pozostają kontekstowe dla zgodności z istniejącym kodem. W odbiorze M13.2
pakiet testów kompilatora przechodził 270/270. M13.2 nie generuje SQL, nie wybiera
providera i nie wykonuje planu. Zakres opisuje
[`43-storage-source-projection.md`](43-storage-source-projection.md).

M13.3 ma status `VERIFIED COMPILER CONTRACT`. Lowerer tworzy deterministyczny
deskryptor `query.plan` / `read` dla istniejącego
`junexis.storage.runtime/0.1`, z wejściem
`junexis.storage.query-invocation/0.1` i wymaganym capability `query-plan`.
Dokument zachowuje model, kroki w kolejności wykonania, wynikowy shape oraz
kanoniczne argumenty wszystkich dziewięciu typów ABI. Walidacja `SQPxxx` i
`SQRxxx` zatrzymuje niepoprawny plan, brak, duplikat, obcy argument, niezgodny
typ, nullability i błędną wartość. Sześć testów kontraktu oraz pełna bramka
rozwiązania 729/729 przechodzą. Host i providery nie wykonują jeszcze
`query.plan`; to granica następnego pionu.
Zakres opisuje
[`44-storage-runtime-query-lowering.md`](44-storage-runtime-query-lowering.md).

M13.4 ma status `VERIFIED EXECUTION SLICE`. Runtime SDK definiuje capability
`query-plan` i stabilne identyfikatory kontraktu, Host wymusza capability dla
`query.plan`, a provider memory wykonuje `filter`, `project`, `sort` i `limit`
na snapshotcie logicznej encji. Sprawdza wersje, argumenty, dziewięć typów ABI,
kształt rekordów i wynik, po czym zwraca typowane wiersze przez kursor. Test
obejmuje rzeczywisty Host oraz spakowany provider uruchomiony w osobnym
procesie. SQLite i PostgreSQL nie wykonują jeszcze Query Plan. Zakres opisuje
[`45-storage-memory-query-execution.md`](45-storage-memory-query-execution.md).

M13.5 ma status `VERIFIED MULTI-PROVIDER EXECUTION`. Referencyjna semantyka
Query Plan jest współdzielonym assembly paczek providerów, a memory i SQLite
przechodzą ten sam test parametru, literału, nullable, predykatu logicznego,
projekcji, wielokluczowego sortu, limitu i widoczności transakcyjnej. SQLite
materializuje trwały, jawnie ograniczony snapshot encji i zwraca ten sam kursor;
procesowe E2E potwierdza odczyt przez `query.plan` po restarcie Host. Na końcu
M13.5 PostgreSQL i SQL pushdown pozostawały otwarte; pierwszy bezpieczny
podzbiór pushdown realizuje M13.6. Zakres M13.5 opisuje
[`46-storage-sqlite-query-conformance.md`](46-storage-sqlite-query-conformance.md).

M13.6 ma status `VERIFIED SAFE QUERY PUSHDOWN`. Provider SQLite obniża
dowiedziony podzbiór `filter`, `project`, `sort` i `limit` do
parametryzowanego SQL. Plany z semantyką nullable, porządkiem tekstowym lub
nieobsługiwanym układem kroków wracają w całości do wspólnego kernela. Przed
SQL provider strumieniowo waliduje źródło i budżety, a obie fazy wiąże jednym
snapshotem transakcyjnym. Testy obejmują SQL injection, ścieżkę natywną,
fallback i procesowe E2E. PostgreSQL pozostaje otwarty. Zakres opisuje
[`47-storage-sqlite-query-pushdown.md`](47-storage-sqlite-query-pushdown.md).

M13.7 ma status `VERIFIED THREE-PROVIDER EXECUTION`. Provider PostgreSQL
deklaruje `query-plan`, pakuje wspólny kernel i materializuje ograniczony,
uporządkowany snapshot aktywnej transakcji. Wspólna bramka zawiera trzeci test
parametru, nullable, timestamp, sortu, limitu i rollbacku, ale uruchamia go
wyłącznie z `JUNEXIS_TEST_POSTGRESQL`. 23 sierpnia 2026 bramka przeszła na
izolowanej bazie PostgreSQL 17.5: 37/37 testów, bez pominięć, w tym podstawowe
live E2E, portable-store conformance i Query Plan conformance. Tymczasowa baza
i rola zostały usunięte, a kontrola zwróciła zero pozostałych zasobów. Zakres opisuje
[`48-storage-postgresql-query-execution.md`](48-storage-postgresql-query-execution.md).

M13.8 ma status `VERIFIED SAFE QUERY PUSHDOWN`. Provider PostgreSQL obniża
dowiedziony podzbiór `filter`, `project`, `sort` i `limit` do typowanego,
parametryzowanego SQL nad JSONB. Pozostałe plany wykonuje w całości wspólny
kernel. Preflight i SQL widzą jeden snapshot `RepeatableRead`, również w jawnej
transakcji aplikacyjnej. Live gate na PostgreSQL 17.5 przeszedł 41/41 testów
bez pominięć, w tym SQL injection, dziewięć typów, widoczność transakcyjną i
rollback. Zakres opisuje
[`49-storage-postgresql-query-pushdown.md`](49-storage-postgresql-query-pushdown.md).

M13.9 ma status `VERIFIED GENERATED E2E`. Generator `@server` tworzy dla
każdego named query osobne typy parametrów i wyniku oraz funkcję Go, która
wiąże wartości uruchomieniowe, wysyła `query.plan` przez istniejącego klienta
Storage Runtime i konsumuje kursor do końca. Golden gate kompiluje źródło
`.jnx`, uruchamia referencyjny Host i provider memory w osobnym procesie,
zapisuje obserwację i odczytuje ją przez wygenerowane API. Klient nie zawiera
SQL ani zależności od konkretnej bazy. Na granicy M13.9 bezpośrednie wywołanie
query z ciała tasku `.jnx` pozostawało następnym etapem. Zakres opisuje
[`50-storage-generated-query-client.md`](50-storage-generated-query-client.md).

M13.10 ma status `VERIFIED TASK-TO-STORAGE E2E`. Semantyka rejestruje dla
każdego named query kwalifikowaną funkcję oraz syntetyczny typ wiersza końcowej
projekcji. Sprawdza target `@server`, liczbę, nazwy i typy argumentów. Generator
Go otwiera deployment-selected provider tylko wtedy, gdy task rzeczywiście
wywołuje query, porządkuje argumenty nazwane, mapuje wynik do typu tasku i
zamyka provider razem z procesem. Golden E2E przechodzi przez funkcję tasku,
rzeczywisty Host i provider memory. Zakres opisuje
[`51-storage-task-query-binding.md`](51-storage-task-query-binding.md).

M13.11 ma status `VERIFIED MODULAR TASK-TO-STORAGE E2E`. Binder symboli
udostępnia importowaną deklarację `storage`, a semantyka wyprowadza z niej API
named query, jeżeli import glob albo selective zachowuje kanoniczną nazwę
modelu. Moduł aplikacji widzi
syntetyczny typ wiersza i funkcję dokładnie jak dla deklaracji lokalnej, a
composer przekazuje osiągalny model do codegenu dependency-first. Golden gate
buduje projekt z dwoma plikami, uruchamia task wejściowy przez rzeczywisty Host
i provider memory oraz sprawdza odczyt zapisanego rekordu. Alias modelu,
re-export i nawigacja LSP symboli syntetycznych pozostają poza tym etapem.
Zakres opisuje
[`52-imported-storage-query-binding.md`](52-imported-storage-query-binding.md).

M13.12 ma status `VERIFIED ALIAS-TO-CANONICAL E2E`. Selective import
`HomeData as Store` udostępnia w module funkcje `Store_Query` i syntetyczne typy
`Store_QueryRow`. Po sprawdzeniu typów composer przepisuje je do kanonicznych
symboli `HomeData_...` przed Structural IR. Wygenerowany klient, Query Plan,
fingerprint i Runtime nie zawierają aliasu. Golden gate buduje projekt
aliasujący model, uruchamia task przez rzeczywisty Host i provider memory oraz
potwierdza odczyt danych przy zachowanej kanonicznej tożsamości. Alias modułu,
re-export i kwalifikowane wywołania pozostają otwarte. Zakres opisuje
[`53-storage-import-alias.md`](53-storage-import-alias.md).

M13.13 ma status `VERIFIED STORAGE QUERY DEVELOPER EXPERIENCE`. Workspace LSP
wyprowadza z widocznych deklaracji `storage` te same funkcje named query i typy
wierszy, które zna semantyka. Completion respektuje zakres importów i aliasy,
hover pokazuje sygnaturę, wynik, efekt oraz kanoniczną tożsamość Runtime, a Go
to Definition prowadzi z funkcji lub typu wiersza do deklaracji query także w
nieotwartym module. Outline oraz semantic tokens rozpoznają strukturę DSL.
Alias całego modułu otrzymuje ostrzeżenie `SQC006`, ponieważ kwalifikowana
składnia query nie istnieje. Końcowa bramka powtarza testy Core, CLI i LSP oraz
piony M13.10–M13.12. Tym etapem M13 zamyka przenośną ścieżkę odczytu; mutacje,
jawne błędy, transakcje aplikacyjne i raw SQL należą do następnych kamieni
milowych. Zakres opisuje
[`54-storage-query-developer-experience.md`](54-storage-query-developer-experience.md).

M14.1 ma status `VERIFIED COMPILER CONTRACT`. Osobny
`junexis.storage.mutation-plan/0.1` opisuje provider-neutralne `Insert`,
`Update` i `Delete`, typowane parametry, przypisania, predykat oraz jawny zasięg
`Single` albo `Many`. Walidator wymaga pełnego klucza dla `Single`, jawnego
predykatu dla update/delete, kompletu pól non-nullable dla insertu i zabrania
zmiany klucza. Plan nie zawiera SQL, providera ani zgody na automatyczne retry.
M14.1 nie dodaje jeszcze składni `.jnx`, loweringu Runtime ani wykonania.
Zakres opisuje
[`55-storage-mutation-plan-ir.md`](55-storage-mutation-plan-ir.md).

M14.2 ma status `VERIFIED COMPILER CONTRACT`. Gramatyka, AST i Structural IR
zachowują nazwane `mutation` z rodzajem `insert/update/delete`, obowiązkowym
zasięgiem `single/many`, parametrami, przypisaniami `set` i opcjonalnym
`where`. Projektor tworzy Mutation Plan M14.1, uruchamia jego walidację i mapuje
problemy `SMPxxx` na parametr, przypisanie albo predykat w `.jnx`. Wspólny
pipeline analiz publikuje je przez CLI i LSP. Query i
mutation współdzielą przestrzeń nazw operacji modelu. Completion, hover,
semantic tokens oraz Outline rozpoznają nową składnię. M14.2 nadal nie obniża
mutacji do Runtime i nie zmienia danych przez provider. Zakres opisuje
[`56-storage-mutation-source-projection.md`](56-storage-mutation-source-projection.md).

M14.3 ma status `VERIFIED COMPILER CONTRACT`. Lowering tworzy deterministyczne
wywołanie `mutation.plan` z efektem `write`, capability `mutation-plan` i
wejściem `junexis.storage.mutation-invocation/0.1`, bez SQL, providera, bindingu
i sekretów. Query i mutation współdzielą kanoniczny kodek dziewięciu typów
Storage ABI. Jawny wynik `junexis.storage.mutation-result/0.1` rozróżnia
`applied` oraz `not-applied` i zachowuje `affectedRows`; wynik nieznany trafia
wyłącznie do kanału błędu jako `unknown-outcome`. M14.3 nie dodaje jeszcze
capability do Host ani wykonania przez memory, SQLite lub PostgreSQL. Zakres
opisuje
[`57-storage-runtime-mutation-lowering.md`](57-storage-runtime-mutation-lowering.md).

M14.4 ma status `VERIFIED EXECUTION SLICE`. Runtime SDK definiuje capability
`mutation-plan`, Host wymusza parę `mutation.plan/write`, a osobny kernel
`Junexis.Storage.MutationPlan` waliduje i wykonuje `insert/update/delete` na
snapshotcie providera memory. Zapis respektuje `single/many`, logiczny klucz,
limity i transakcję oraz zwraca `applied/not-applied` z `affectedRows`. Test
przez Host potwierdza stan istniejącym `query.plan`, konflikt powtórzonego
klucza, powtórny delete i kompletność paczki. SQLite, PostgreSQL i bezpośrednie
wywołanie mutation z tasku pozostają otwarte. Zakres opisuje
[`58-storage-memory-mutation-execution.md`](58-storage-memory-mutation-execution.md).

M14.5 ma status `VERIFIED PERSISTENT EXECUTION SLICE`. SQLite deklaruje
`mutation-plan`, wykonuje wspólny kernel na ograniczonym snapshotcie i
publikuje zmianę w jednej transakcji. Wspólna bramka potwierdza identyczne
znaczenie insert/update/delete, konfliktu, `not-applied` i rollbacku dla memory
oraz SQLite. Izolowane E2E przechodzi cztery procesy Host i kolejne restarty
jednego pliku bazy, dowodząc trwałości insertu, update i delete. Natywny
pushdown, PostgreSQL i wywołanie z tasku pozostają otwarte. Zakres opisuje
[`59-storage-sqlite-mutation-conformance.md`](59-storage-sqlite-mutation-conformance.md).

M14.6 ma status `VERIFIED SAFE MUTATION DELTA PUSHDOWN`. Po wykonaniu wspólnego
kernela SQLite dowodzi różnicy snapshotów i zapisuje wyłącznie właściwe
parametryzowane INSERT/UPDATE/DELETE. Niespójny albo zbyt duży kształt delty
wraca do atomowego fallbacku M14.5. Test lowerera, procesowe E2E i wspólna
bramka providerów chronią semantykę, trwałość oraz brak interpolacji danych.
Bezpośrednia kompilacja predykatu i mutacje PostgreSQL pozostają otwarte.
Zakres opisuje
[`60-storage-sqlite-mutation-pushdown.md`](60-storage-sqlite-mutation-pushdown.md).

M14.7 ma status `VERIFIED THREE-PROVIDER MUTATION EXECUTION`. PostgreSQL
deklaruje `mutation-plan`, pakuje wspólny kernel, wykonuje plan na ograniczonym
snapshotcie `RepeatableRead` i publikuje wynik atomowo. Wspólna bramka obejmuje
odtąd memory, SQLite i PostgreSQL. Live odbiór na PostgreSQL 17.5 zakończył się
wynikiem 50/50 bez pominięć; tymczasowa baza i rola zostały usunięte, a kontrola
zwróciła zero pozostałości. Mutation pushdown PostgreSQL pozostaje otwarty.
Zakres opisuje
[`61-storage-postgresql-mutation-conformance.md`](61-storage-postgresql-mutation-conformance.md).

M14.8 ma status `VERIFIED SAFE THREE-PROVIDER MUTATION DELTA`. Wspólny assembly
Mutation Plan dowodzi różnicę snapshotów dla SQLite i PostgreSQL. PostgreSQL
mapuje ją na stałe parametryzowane INSERT/UPDATE/DELETE, wymaga jednego
zmienionego wiersza na polecenie i zachowuje atomowy fallback. Live trigger
potwierdza fizycznie `0 INSERT / 1 UPDATE / 0 DELETE` dla aktualizacji jednego
z dwóch rekordów. Bezpośredni predicate pushdown pozostaje otwarty. Zakres
opisuje
[`62-storage-postgresql-mutation-pushdown.md`](62-storage-postgresql-mutation-pushdown.md).

M14.9 ma status `VERIFIED GENERATED MUTATION E2E`. Generator `@server` tworzy
provider-neutralne, typowane parametry, wynik i funkcję Go dla każdej named
mutation. Funkcja wywołuje `mutation.plan/write`, waliduje jawny wynik i nie
zawiera SQL, providera ani sekretów. Golden E2E kompiluje `.jnx`, uruchamia
Runtime Host z providerem memory, wykonuje wygenerowany insert i potwierdza
fizycznie zapisany rekord. Wywołanie named mutation bezpośrednio w ciele tasku
pozostaje zakresem M14.10. Zakres opisuje
[`63-storage-generated-mutation-client.md`](63-storage-generated-mutation-client.md).

M14.10 ma status `VERIFIED TASK-TO-MUTATION E2E`. Named mutation oraz jej
syntetyczny wynik są symbolami języka dostępnymi w tasku `@server`. Semantyka
sprawdza target, liczbę, nazwy i typy argumentów, a generator porządkuje
argumenty nazwane, współdzieli lifecycle providera i wywołuje neutralnego
klienta M14.9. Golden E2E wykonuje task `.jnx`, otrzymuje `applied/1` i
potwierdza fizyczny rekord w providerze memory. Błędy zapisu pozostają
fail-fast bez automatycznego retry. Zakres opisuje
[`64-storage-task-mutation-binding.md`](64-storage-task-mutation-binding.md).

M15.1 ma status „zweryfikowana inwentaryzacja”. Repozytorium `junexis-sdk` utrwala 21 publicznych
typów Abstractions 0.1.0, ich trzy powierzchnie odpowiedzialności, sumy źródeł
oraz wszystkich konsumentów. Inwentaryzacja wykazała, że sześć pomocników
Native SQL zależy od ADO.NET i wymaga późniejszej decyzji pakietowej.

M15.2 ma status „zweryfikowana migracja między repozytoriami”. Jedynym źródłem
`Junexis.Storage.Runtime.Abstractions` jest `junexis-sdk`. Host środowiska
wykonawczego, testy i pięć projektów produkcyjnych dostawców korzystają z
niezmienionej paczki 0.1.0. Runtime nie ma już projektu ani `ProjectReference`
do Abstractions. Pakiet Hosta pozostaje własnością Runtime. Zgodność chronią
testy Runtime i pełna bramka dostawców. Zakres opisuje
[`65-sdk-repository-ownership.md`](65-sdk-repository-ownership.md).

M15.3 ma status „zweryfikowane lokalne tworzenie rozszerzeń”. Pakowany komponent
`Junexis.SDK.Tool` realizuje kontrakt `junexis sdk new provider` i generuje
działającego dostawcę z obsługą odczytu, zapisu i kontroli gotowości, zależnego
wyłącznie od publicznego Abstractions 0.1.0. Wynik zawiera manifest, README i
test uruchomieniowy, nie deklaruje transakcji i nie nadpisuje istniejącej
ścieżki. Bramka wykonuje wygenerowanego dostawcę i kontroluje zawartość jego
`.nupkg`. Pełny zestaw testów zgodności pozostaje zakresem M15.4. Szczegóły opisuje
[`66-sdk-provider-template.md`](66-sdk-provider-template.md).

M15.4 ma status „zweryfikowana przenośna zgodność”. Polecenie
`junexis sdk test provider` buduje i ładuje zewnętrznego dostawcę bez zależności
od Runtime, kompilatora i oficjalnych sterowników. Jedenaście kontroli obejmuje
fabrykę, deskryptor, ABI, zdolności, wiązanie, kontrolę gotowości, anulowanie,
błędy, odczyt, zapis i transakcje. Raport `junexis.sdk.provider-conformance/0.1`
rozróżnia niezgodność kodem `3` od błędu przygotowania kodem `2`. Bramka
negatywna wykrywa fałszywie zadeklarowane transakcje. Zakres opisuje
[`67-sdk-provider-conformance.md`](67-sdk-provider-conformance.md).

M15.5 ma status „zweryfikowany ścisły manifest”. Publiczne schematy JSON formalizują
`junexis.provider/0.1` i scenariusz testów zgodności. Polecenie
`junexis sdk validate provider` kontroluje SemVer, ABI, typ fabryki,
zdolności, zależność SDK, artefakt oraz zgodność produkcyjnego `.csproj`, w
tym zakaz `ProjectReference`. Stabilne diagnostyki `JXMxxxx` mają raport
`junexis.sdk.provider-validation/0.1` i kod niezgodności `4`. Test zgodności nie
uruchamia obcego kodu po błędnej kontroli wstępnej. Zakres opisuje
[`68-sdk-provider-manifest-validation.md`](68-sdk-provider-manifest-validation.md).

M15.6 ma status „zweryfikowane rozstrzyganie zgodności”. Publiczna macierz wiąże
manifest 0.1, SDK 0.1.0, ABI Storage Runtime 0.1, Runtime Host 0.1.x oraz
`net10.0`. Polecenie `junexis sdk compatibility provider` przed wykonaniem
providera sprawdza też docelowy RID i zdolności wymagane przez aplikację.
Raport `junexis.sdk.provider-compatibility/0.1` używa diagnostyk `JXC` z
wskazówkami naprawy oraz kodu niezgodności `5`. Zakres opisuje
[`69-sdk-provider-compatibility.md`](69-sdk-provider-compatibility.md).

M15.7 ma status „zweryfikowany profil planów magazynu”. Jawny profil zestawu
testów zgodności wykonuje w jednej sesji kroki `query.plan` i `mutation.plan`.
Kontroluje
efekty, rodzaj wyniku oraz dokładny JSON wartości i kursora. Bramka pozytywna
potwierdza rytm odczyt–mutacja–odczyt, a negatywna wykrywa fałszywy stan
końcowy. Zakres opisuje
[`70-sdk-storage-plans-conformance.md`](70-sdk-storage-plans-conformance.md).

M15.8 ma status „zweryfikowane włączenie rzeczywistych dostawców”. Manifest
autora 0.2 obsługuje krótki identyfikator Runtime, osobny identyfikator paczki,
jawny projekt i pełne metadane dystrybucji. Memory, SQLite i PostgreSQL mają jedno źródło JSON i nie używają
produkcyjnych `ProjectReference`; dwa wspólne kernele są lokalnymi paczkami.
Zestaw testów zgodności SDK wykonuje rzeczywiste Memory i SQLite, a scenariusz
PostgreSQL bezpiecznie wymaga wartości poufnej przez `${env:NAME}`. Zakres opisuje
[`71-sdk-real-provider-onboarding.md`](71-sdk-real-provider-onboarding.md).

M15.9 ma status „zweryfikowane rzeczywiste plany Memory i SQLite” oraz
„PostgreSQL gotowy do próby na rzeczywistej bazie”. Jeden scenariusz wykonuje
na rzeczywistych dostawcach sekwencję
brak–wstawienie–odczyt–usunięcie–brak i porównuje dokładne wyniki Query Plan
oraz Mutation Plan. Wartości sesji izolują powtórne uruchomienia; Memory i SQLite
przechodzą po dwa przebiegi. W bieżącym środowisku nie ma wartości poufnej PostgreSQL,
dlatego jego identyczny profil jest gotowy do próby na rzeczywistej bazie, ale
nie jest fałszywie oznaczony jako wykonany. Zakres opisuje
[`72-sdk-real-storage-plans-conformance.md`](72-sdk-real-storage-plans-conformance.md).

M15.10 ma status „zweryfikowany lokalny kandydat wydania”. Publiczny schemat
`junexis.sdk.release/0.1` wiąże wersję z dokładnym zestawem paczek i SHA-256.
Zainstalowane z paczki polecenie `junexis sdk verify release` sprawdza również
ID i wersję `.nuspec`; czysty konsument buduje się tylko ze źródła paczek
kandydata.
Bramka odrzuca zmienioną paczkę oraz niezadeklarowany artefakt. Nie oznacza to
jeszcze podpisanego, odtwarzalnego ani publicznie hostowanego wydania. Zakres
opisuje [`73-sdk-release-candidate.md`](73-sdk-release-candidate.md).

M15.11 ma status „zweryfikowany odtwarzalny lokalny proces budowania”. Dwa
niezależne procesy budowania z tej samej rewizji, konfiguracji i wersji .NET SDK
tworzą identyczne bajty obu paczek, manifestu oraz informacji o pochodzeniu.
`junexis sdk verify provenance` kontroluje związek z manifestem i odróżnia
czyste od zmienionego drzewa źródeł. Zakres nie
obejmuje jeszcze odtwarzalności międzyplatformowej ani podpisu kryptograficznego.
Szczegóły opisuje [`74-sdk-reproducible-build.md`](74-sdk-reproducible-build.md).

M15.12 ma status „zweryfikowany lokalny podpis zestawu wydania”. Dokument
`junexis.sdk.signature/0.1` wiąże manifest i informację o pochodzeniu podpisem
ECDSA P-256 oraz jawnym identyfikatorem klucza. `junexis sdk verify signature`
odrzuca zmienione artefakty, obcy klucz i obcy identyfikator kodem `8`.
Klucz prywatny pozostaje poza wydaniem, a ponowny proces budowania usuwa stary
podpis. Oficjalny klucz projektu, rotacja, unieważnianie i publiczne źródło
zaufania nie należą jeszcze do tego etapu. Szczegóły opisuje
[`75-sdk-signed-release.md`](75-sdk-signed-release.md).

M15.13 ma status „zweryfikowane lokalne zasady zaufania do wydań”. Zewnętrzny
`junexis.sdk.trust-store/0.1` wiąże klucze wydawnicze z przeznaczeniem,
okresem ważności i statusem. `junexis sdk verify trust` odrzuca klucz wygasły,
unieważniony, nieznany i podmieniony kodem `9`. Minimalna znana rewizja chroni
przed cofnięciem polityki, a magazyn znajdujący się w badanym wydaniu jest
odrzucany jako samodzielna deklaracja zaufania. Oficjalny magazyn projektu i
jego bezpieczna dystrybucja pozostają poza etapem. Szczegóły opisuje
[`76-sdk-release-trust.md`](76-sdk-release-trust.md).

M15.14 ma status „zweryfikowany lokalny łańcuch zaufania”. Oddzielny klucz
główny podpisuje kontrakt `junexis.sdk.trust-store-signature/0.1`, a
`junexis sdk verify trust` w raporcie 0.2 przechodzi od klucza głównego przez
politykę i klucz wydawniczy do zestawu SDK. Bramka odrzuca zmianę magazynu,
obcy klucz główny i podpisaną rewizję starszą od wymaganej. Klucz prywatny nie
trafia do publikacji. Oficjalny klucz główny i zaufany kanał jego dystrybucji
pozostają poza etapem. Szczegóły opisuje
[`77-sdk-authenticated-trust-store.md`](77-sdk-authenticated-trust-store.md).

M15.15 ma status „M15 ZAMKNIĘTY”. `scripts/verify-m15.ps1` wykonuje testy
rozwiązania SDK, bramki M15.1–M15.14 oraz kontrolę publicznych schematów i
dokumentów. Zamknięty zakres obejmuje własność ABI, narzędzia autora,
zgodność rzeczywistych dostawców, odtwarzalny zestaw wydania i lokalny
łańcuch zaufania. Publiczne źródło paczek, oficjalny klucz główny, instalator
i operacyjna publikacja nie są częścią M15. Szczegóły opisuje
[`78-sdk-m15-closure.md`](78-sdk-m15-closure.md).

M16.1 ma status „zweryfikowany kontrakt wydania platformy”. Meta-repozytorium
`junexis-platform` jest właścicielem schematu `junexis.platform-release/0.1`,
generatora manifestu Windows i ścisłego walidatora. Kontrakt rozróżnia artefakt
dostępny, komponent osadzony oraz komponent odroczony. Bramka obejmuje poprawny
zestaw i dziewięć prób negatywnych, między innymi zmianę artefaktu, cykl
zależności, niebezpieczną ścieżkę i fałszywe oznaczenie gotowości. Bieżący
kandydat pozostaje lokalny; M16.1 nie oznacza jeszcze kompletnej paczki ani
publicznej publikacji. Szczegóły opisuje
[`79-platform-release-contract.md`](79-platform-release-contract.md).

M16.2 ma status „zweryfikowany zestaw zgodności platformy”. Publiczny katalog
`junexis-0.1-preview` obejmuje trzynaście komponentów, Windows x64, zakres
SemVer linii 0.1 oraz kontrakty SDK, dostawców magazynu i Laboratorium. Resolver
porównuje kanał, środowisko docelowe, kompletny inwentarz, role, wymagalność, stany, wersje,
kontrakty i zależności. Bramka zalicza pełny zestaw i odrzuca dziesięć klas
niezgodności. Generator dystrybucji uruchamia M16.1 oraz M16.2 przed uznaniem
manifestu za końcowy. Szczegóły opisuje
[`80-platform-release-compatibility.md`](80-platform-release-compatibility.md).

M16.3 ma status „zweryfikowana samodzielna paczka LSP Windows x64”. Skrypt
publikacji przyjmuje wersję całego wydania, buduje ReadyToRun w trybie
`--self-contained` i zapisuje `junexis-lsp.json`. Platformowy walidator uruchamia
EXE, porównuje jego tożsamość z manifestem i odrzuca pięć klas uszkodzenia.
Generator dystrybucji tworzy osobny ZIP i zmienia stan komponentu `lsp` z
`deferred` na `available` dopiero po przejściu próby. Instalacja i automatyczne
podłączenie klientów nie należą do M16.3. Szczegóły opisuje
[`81-platform-lsp-distribution.md`](81-platform-lsp-distribution.md).

M16.4 ma status „zweryfikowana instalacja i automatyczne wykrywanie LSP”.
`junexis install --lsp --local-packages <katalog>` instaluje wersjonowany ZIP
transakcyjnie do profilu użytkownika. Walidacja przed podmianą uruchamia LSP i
porównuje jego tożsamość; błąd zachowuje poprzednią wersję. Studio i rozszerzenie
VS Code używają wspólnego kontraktu ścieżki. Bramka M16.4 buduje rzeczywistą
paczkę, instaluje ją w izolowanym profilu i uruchamia po instalacji. Pobieranie
sieciowe pozostaje poza etapem. Szczegóły opisuje
[`82-platform-lsp-installation.md`](82-platform-lsp-installation.md).

M16.5 ma status „zweryfikowane rozszerzenie VS Code w wydaniu platformy”.
Generator tworzy wersjonowany `junexislang-<wersja>.vsix` bez drugiej kopii
LSP. Instalator przed wywołaniem programu `code` porównuje tożsamość i wersję w
obu manifestach, sprawdza punkt wejścia i zasoby języka oraz odrzuca
niebezpieczne archiwum. `junexis install --extension` dodaje najpierw wspólne
LSP, z którego korzystają również Studio i VS Code. Marketplace, podpis i
pobieranie sieciowe pozostają poza etapem. Szczegóły opisuje
[`83-platform-vscode-extension.md`](83-platform-vscode-extension.md).

M16.6 ma status „zweryfikowany zestaw Runtime”. Artefakt
`junexis-runtime-<wersja>-win-x64.zip` zawiera zgodne paczki
`JunexisLang.Compiler.Core` i `JunexisLang.Runtime`, źródła embedded C oraz
crate embedded Rust. Manifest `junexis.runtime.release/0.1` wiąże trzy warstwy
z zamkniętym inwentarzem i sumami SHA-256. Bramka wykonuje czystego konsumenta
.NET, test zgodności C i pełne testy biblioteki Runtime. Rust pozostaje jawnie
`source-only`, ponieważ lokalne środowisko wydawnicze nie ma `cargo`. Nie
oznacza to jeszcze przepięcia bootstrapowych warstw Go, TypeScript i C++ ani
instalacji paczek. Szczegóły opisuje
[`84-platform-runtime-distribution.md`](84-platform-runtime-distribution.md).

M16.7 ma status „zweryfikowany zestaw SDK”. Artefakt
`junexis-sdk-<wersja>-win-x64.zip` zawiera paczkę neutralnych abstrakcji
Storage Runtime, narzędzie autora, manifest `junexis.sdk.release/0.1` oraz
informację o pochodzeniu. Bramka instaluje narzędzie wyłącznie z badanego
zestawu, uruchamia jego własne weryfikatory i wykonuje czystego konsumenta ABI.
SDK nie jest jeszcze instalowane do profilu użytkownika ani podpisane
oficjalnym kluczem projektu. Dostawcy baz danych pozostają osobnymi
komponentami. Szczegóły opisuje
[`85-platform-sdk-distribution.md`](85-platform-sdk-distribution.md).

M16.8 ma status „zweryfikowane zestawy dostawców Storage Runtime”. Memory,
SQLite i PostgreSQL mają osobne artefakty, pozostają poza SDK i zależą od
komponentów `sdk` oraz `runtime`. Rachunek `junexis.provider.release/0.1`
wiąże wersję dostawcy `0.1.0` z paczką SDK konkretnego wydania platformy,
rewizją źródła i zamkniętym inwentarzem SHA-256. Publiczny walidator SDK
akceptuje trzy manifesty, a czysty konsument ładuje każdą fabrykę. Test live
PostgreSQL nadal wymaga jawnie dostarczonego serwera i sekretu. Szczegóły
opisuje [`86-platform-provider-distribution.md`](86-platform-provider-distribution.md).

M16.9 ma status „zweryfikowana instalacja i wykrywanie dostawców”. Nadrzędne
polecenie `junexis provider` instaluje paczkę M16.8 transakcyjnie, prowadzi
rejestr `junexis.provider-registry/0.1`, rozwiązuje implementację według
dokładnej wersji SDK i usuwa jedną rodzinę zgodności bez naruszania pozostałych.
Rejestr pozostaje niezależny od generatora `@server` i nie zawiera sekretów.
Szczegóły opisuje
[`87-platform-provider-installation.md`](87-platform-provider-installation.md).

M16.10 ma status „zweryfikowane rozwiązywanie dostawcy projektu”. Manifest
projektu deklaruje `providerSdkVersion`, a powiązanie Storage wybiera identyfikator
i wersję dostawcy. `doctor` oraz `build` wymagają dokładnego dopasowania w
rejestrze M16.9, obsługują manifest `junexis.provider/0.2` i zapisują logiczne
źródło `profile:` bez ujawniania ścieżki użytkownika. Lokalny mechanizm
`providerPackages` pozostaje zgodny wstecz. Szczegóły opisuje
[`88-platform-project-provider-resolution.md`](88-platform-project-provider-resolution.md).

M16.11 ustanowiło lokalne odtwarzanie zależności projektu. M17.5.4 rozszerza
publiczne
`junexis restore` należy do Installera: deleguje pakiety aplikacyjne do
prywatnego compilera, a następnie instaluje dokładne wersje providerów. Źródłem
może być lokalny zestaw wydania albo wspólne Registry wskazane przez
`--registry`; tryb `--offline` pozostaje ścieżką cache i lockfile bez sieci.
Bramka potwierdza deterministyczny lockfile, powtarzalność profilu providerów i
brak jego zmiany po błędzie compilera.
Szczegóły opisuje [`89-platform-project-restore.md`](89-platform-project-restore.md).

M16.12 ma status „M16 ZAMKNIĘTY”. `scripts/verify-m16.ps1` w
meta-repozytorium wykonuje bramki M16.1–M16.11 oraz kontrolę dokumentów.
Zamknięty zakres obejmuje lokalny zestaw Windows x64, zgodność komponentów,
instalację narzędzi i providerów oraz jedno publiczne odtwarzanie zależności
używane także przez Studio. Wynik pozostaje lokalnym kandydatem; publiczna
publikacja, oficjalny podpis, źródła sieciowe oraz kolejne systemy operacyjne
pozostają poza M16. Szczegóły opisuje
[`90-platform-m16-closure.md`](90-platform-m16-closure.md).

## Aktywny kierunek: M8 Semantic Conformance

M8 stabilizuje znaczenie programu między backendami bootstrapowymi. M8.1 jest
`VERIFIED`: wersjonowany ledger wiąże katalogi kontraktów
`chan`/`queue`/`stream` z natywnymi testami C++/Go/TypeScript, a raport
`junexis.semantic-conformance/0.1` zachowuje hash ledgeru i jawne poziomy
dowodu. Bieżący stan semantyczny pozostaje `PARTIAL`: `chan=S1`, `queue=S2`,
`stream=S2`, podczas gdy celem scenariuszy jest S3.

M8.2 jest `VERIFIED` dla minimalnego Typed Semantic IR 0.1. Osobna projekcja
Structural IR utrwala backend-independent types, effect category, task
lifecycle, kanoniczną semantykę operacji komunikacyjnych i source origin.
Nie jest to jeszcze pełne type inference ani zamiana platform lowering;
rozszerzenie zachowania wartości i control flow należy do M8.3.

M8.3 jest `VERIFIED` dla reprezentatywnej macierzy wartości i control flow.
Ten sam program wygenerowany do C++, Go i TypeScript zwraca identyczny
canonical JSON dla `Int`, `Float`, `Text`, `Bool`, operatorów arytmetycznych,
porównań, logiki oraz `if/else-if/else`. Jest to pierwszy scenariusz S3;
zbiorczy conformance pozostaje `PARTIAL` z wynikiem `1/4`.

M8.4 jest `VERIFIED` dla kontraktu `junexis.task-lifecycle/0.1`. Natywny
scenariusz C++/Go/TypeScript potwierdza równoważny bounded lifecycle, stan
utrzymany pomiędzy wywołaniami, interwał `Duration` w milisekundach oraz
pierwszy tick po interwale. Jest to czas logiczny bez gwarancji dokładności
zegara ściennego. Zbiorczy conformance pozostaje `PARTIAL`, obecnie `2/5`.

M8.5 jest `VERIFIED` dla `chan<T>` jako bounded FIFO z producer backpressure i
retencją do konsumpcji. C++, Go i TypeScript wykonują jeden porównywany
scenariusz S3. C++ nie używa już nieograniczonego deque, a TypeScript nie
udaje backpressure przez zwykłe `push`. Zbiorczy conformance pozostaje
`PARTIAL`, obecnie `3/5`.

M8.6 jest `VERIFIED` dla `queue<T>`: stabilny Message ID do ACK, ponowne
dostarczenie po NACK, brak skutku błędnego ACK i FIFO po prawidłowym ACK są
równoważne na C++, Go i TypeScript. C++ posiada teraz delivery frame oraz
ukryty binding ACK/NACK. Zbiorczy conformance pozostaje `PARTIAL`, `4/5`.

M8.7 jest `VERIFIED` dla `stream<T>`: latest-wins, niezależny fanout, brak
replay historii dla późnej subskrypcji i brak duplikatu bez nowej publikacji
są równoważne na C++, Go i TypeScript. Ledger osiąga `5/5` scenariuszy S3 i
status celu `SATISFIED`; cały etap M8 pozostaje otwarty do M8.8–M8.10.

M8.8 jest `VERIFIED` dla reprezentatywnej granicy `server → report`. Typowany
`extern` wykonuje się i jest wstrzykiwany wyłącznie na targetcie źródłowym,
Typed Semantic IR zachowuje jego efekt przed komunikacją, a wynik przekracza
granicę przez wspólny kontrakt `junexis-wire/0.2`. Wygenerowany odbiorca
TypeScript odrzuca obcą wersję. Scenariusz osiąga S4, raport `6/6`; cały etap
M8 pozostaje otwarty do prezentacji statusu M8.9 i golden E2E M8.10.

M8.9 jest `VERIFIED`: CLI, Mentor i Studio prezentują ten sam status Semantic
Conformance oraz jawne luki. Mentor schema 1.2 utrzymuje opinię oddzielnie od
findings z `blocking=false`, a Studio pokazuje ją w osobnej karcie. Poprawny
raport `partial` nie zmienia wyniku kompilacji.

M8.10 jest `VERIFIED`: raport `junexis.semantic-conformance/0.2` wiąże przez
SHA-256 Build ID, Bundle ID i hash ledgeru. CLI odrzuca zmienioną tożsamość,
Mentor wymaga Build ID zgodnego z Analysis Snapshot, a Studio jawnie odróżnia
`BOUND` od `UNBOUND`. Golden Semantic E2E obejmuje pakowanie trzech targetów,
podpis, conformance, opinię Mentora, kontrolowaną próbę manipulacji, deployment
i obserwację jednej ścieżki C++ → Go → TypeScript.

M8 jest zamknięty dla udokumentowanego, reprezentatywnego podzbioru. Ten status
nie deklaruje stabilności 1.0, pełnej semantyki języka ani produkcyjnego
runtime'u.

## M19.10.1 — przenośna tożsamość zależności

M19.10.1 ma status `VERIFIED` dla granicy wyboru wariantu w CLI. Istniejący
`junexis.packages.lock/0.1` pozostaje niezależny od systemu: przechowuje
logiczne `id + version`, bez RID-u, architektury, adresu pobierania i lokalnej
ścieżki. `PlatformVariantSelector` łączy tę tożsamość z dokładnym wariantem
`junexis.platform-release-catalog/0.2` dopiero dla wskazanego RID-u.

Sześć prób automatycznych potwierdza niezmienność blokady przy wyborze Windows i
Linux, odrzucenie pola RID, brak powrotu do obcego wariantu, wymaganie dokładnej
wersji oraz odrzucenie duplikatu, sprzecznych pól platformy i
niezweryfikowanego podpisu systemowego.
Kryptograficzna autentyczność katalogu pozostaje wcześniejszą bramką M18, a
porównanie semantyki systemu należy do M19.10.2. Szczegóły opisuje
[`92-platform-variant-selection.md`](92-platform-variant-selection.md).

## Zasada publikacji kolejnych statusów

Junexis nie otrzyma statusu stabilnego wyłącznie na podstawie kompletności
dokumentacji albo liczby testów jednostkowych. Stabilizacja będzie wymagała:

1. spójności specyfikacji z gramatyką,
2. kontrolowanych diagnostyk bez awarii procesu,
3. określonego modelu typów i komunikacji,
4. jawnych granic awarii i gwarancji runtime,
5. pionowych, powtarzalnych scenariuszy E2E,
6. zdefiniowanej polityki kompatybilności.

## Uzupełnienie 2026-09-25 — lokalna sesja i panel

CLI `run` publikuje wspólny kontrakt `junexis.panel-ready/1` dla zadeklarowanego
panelu serwera i report. Studio i VS Code mają konsumentów oraz nadzór sesji.
Zakres nie oznacza zamknięcia M19/M20 ani dowodu odbioru UI na wszystkich systemach.
Zobacz [kontrakt CLI](12-cli-and-lsp.md). Raport prób nie jest częścią tego repozytorium.
