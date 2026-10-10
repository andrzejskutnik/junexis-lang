# Narrative runtime trace — kontrakt M6.1–M6.16

> **Status:** `PARTIAL` jako cały subsystem Narrative, lecz kontrakt M6.2 jest
> zaimplementowany dla C++, Go i TypeScript. Artefakty C++ i TypeScript mają
> wykonywalne testy, a aktywna gałąź HTTP adaptera ESP32 (z URL i build ID)
> przeszła rzeczywisty build PlatformIO. M6.3 potwierdza jeden wykonywalny
> łańcuch C++ → Go → TypeScript przez prawdziwy TraceCollector. M6.4 wiąże
> Diagnostic Story z Mentorem wyłącznie przez zgodny build ID i stabilne symbole.

## Cel

Narrative łączy intencję zapisaną w modelu Junexis z dowodem wykonania
wygenerowanego systemu. M6.1 nie jest pełnym debuggerem ani systemem
telemetrycznym. Ustanawia mały, wspólny kontrakt, dzięki któremu artefakt może
jawnie przekazać TraceCollectorowi:

- który task wykonał operację,
- gdzie operacja powstała w źródle `.jnx`,
- do której Narrative należy,
- jaki rodzaj komunikacji wystąpił,
- z którym buildem należy związać zdarzenie.

Treść payloadu nie jest wysyłana. Dla komunikacji kanałowej generowanej w Go
`payloadHash` jest deterministycznym skrótem SHA-256 reprezentacji JSON tej
samej wartości po stronie `send` i `receive`. Pozwala to TraceCollectorowi
połączyć rzeczywiste przekazanie komunikatu bez ujawnienia payloadu. Skrót jest
tokenem korelacyjnym, a nie podpisem ani dowodem integralności. Granice
uruchomienia tasków nadal używają stabilnych tokenów `startup-N`.

## Wybór polityki

Generator rozstrzyga politykę na podstawie pierwszego bloku `narrative`.

```junexis
narrative OrderFlow {
    trace full
}
```

| Deklaracja | Polityka |
|------------|----------|
| brak Narrative | `off` |
| `trace off` lub `trace none` | `off` |
| `trace causal` lub `trace minimal` | `minimal` |
| `trace full` | `full` |
| Narrative bez `trace` | `minimal` dla kompatybilności |

W baseline M6.1 `minimal` i `full` emitują ten sam bezpieczny zestaw punktów:
granice uruchomienia tasków, jawne operacje `send` oraz odbiory z kanałów.
Rozróżnienie pozostaje w kontrakcie, aby późniejsze rozszerzenie `full` nie
zmieniało znaczenia `minimal`.

## Protokół artefaktu

Model zdarzeń ma identyfikator `junexis.tracecollector.v1`.

Artefakt odczytuje:

- `JUNEXIS_TRACE_URL` — bazowy adres TraceCollectora; pusty oznacza no-op,
- `JUNEXIS_BUILD_ID` — opcjonalny identyfikator buildu, przekazywany jako
  atrybut `build_id`,
- `JUNEXIS_BUNDLE_ID` — opcjonalny identyfikator zweryfikowanego System Bundle,
  przekazywany jako atrybut `bundle_id`.

Go i TypeScript odczytują te wartości ze środowiska procesu. Mikrokontroler nie
ma równoważnego środowiska, dlatego C++ używa definicji kompilatora o dokładnie
tych samych nazwach:

```ini
build_flags =
    -DJUNEXIS_TRACE_URL=\"http://collector:8080\"
    -DJUNEXIS_BUILD_ID=\"build-42\"
    -DJUNEXIS_BUNDLE_ID=\"bundle-17\"
```

Bez `JUNEXIS_TRACE_URL` adapter embedded pozostaje no-op. Runtime C++ udostępnia
również `set_trace_sink`, co pozwala testom i docelowym transportom zastąpić
HTTP bez zmiany kodu tasków.

Zdarzenie jest wysyłane przez `POST /events`:

```json
{
  "taskId": 1,
  "kind": "COMM_SEND",
  "lamportClock": 0,
  "file": "src/main.jnx",
  "line": 12,
  "function": "Publish",
  "attributes": {
    "narrative_id": "OrderFlow",
    "payloadHash": "Publish:12:...",
    "type": "Text",
    "build_id": "build-...",
    "bundle_id": "bundle-..."
  }
}
```

M6.1 używa istniejących rodzajów `COMM_SEND` i `COMM_RECV`. Nie wprowadza
pozornego `TASK_START`, którego TraceCollector nie obsługuje. Po przyjęciu
zdarzenia artefakt odpytuje `GET /debug/session`; status `Paused` tworzy
dyskretną bramkę debuggera, a każdy inny status pozwala kontynuować.

Instrumentacja jest **fail-open**: brak URL, timeout, błąd sieci, błędna
odpowiedź albo niedostępny collector nie mogą zatrzymać logiki użytkownika.
Timeout pojedynczej operacji wynosi 2 sekundy.

## Tożsamość i mapowanie

- `taskId` jest 1-based i wynika z kolejności tasków w Structural IR.
- `startup-N` także wynika z globalnej kolejności Structural IR, a nie z
  lokalnej kolejności tasków danego backendu. Dzięki temu granica między C++,
  Go i TypeScript zachowuje ten sam token korelacyjny.
- `file` i `line` pochodzą z kanonicznej source mapy IR.
- `function` jest nazwą tasku.
- `narrative_id` jest etykietą pierwszej Narrative.
- `payloadHash` koreluje parę zdarzeń lub identyfikuje punkt wysłania; nie
  zawiera wartości biznesowej.

Te same nazwy są publikowane w `debug/manifest.toml`, dzięki czemu Studio,
Mentor i TraceCollector mogą pracować bez backendowych wyjątków.

## Macierz M6.1

| Backend | Polityka opt-in/out | Granica tasku | `send` | Test wykonania |
|---------|---------------------|---------------|--------|----------------|
| Go `@server` | ✅ | ✅ | ✅ | ✅ rzeczywisty TraceCollector |
| TypeScript `@report` | ✅ | ✅ | ✅ | ✅ `tsc` + Node |
| C++ `@embedded` | ✅ | ✅ | ✅ | ✅ `g++` + aktywny HTTP w PlatformIO ESP32-S3 |

Adapter C++ używa HTTPClient i ArduinoJson wyłącznie w kompilacji ESP32 z
ustawionym `JUNEXIS_TRACE_URL`. Błędy połączenia, odpowiedzi i deserializacji
pozostają fail-open. Hostowy runtime może działać bez obu bibliotek.

Go oblicza ten sam skrót przed przekazaniem wartości do kanału i bezpośrednio
po jej odebraniu. TraceCollector może dzięki temu zrekonstruować granice
`task producenta → task konsumenta`. Błąd serializacji tworzy jawny token
`unavailable:<typ>` i nie zatrzymuje programu.

## Dowód M6.3

Skrypt platformowy `scripts/test-narrative-runtime-e2e.ps1`:

1. buduje bieżący Compiler CLI i TraceCollector Server,
2. kompiluje jeden system `.jnx` do wszystkich trzech targetów,
3. uruchamia wygenerowany task C++ z hostowym sinkiem transportowym,
4. uruchamia wygenerowane Go i TypeScript z `JUNEXIS_TRACE_URL`,
5. sprawdza dokładnie pięć eventów z tym samym build ID i Narrative,
6. wymaga łańcucha `/why` o głębokości co najmniej 4,
7. sprawdza Diagnostic Story wraz z runtime build ID; M7.8 rozszerza bieżący
   kontrakt do 1.2 i pełnej pary Build ID / Bundle ID.

Zweryfikowany łańcuch:

```text
C++ COMM_SEND startup-0
  → Go COMM_RECV startup-0
  → Go COMM_SEND startup-1
  → TypeScript COMM_RECV startup-1
```

Jest to dowód kontraktu i rekonstrukcji przyczynowej, nie pełna symulacja
transportu biznesowego ani fizyczne uruchomienie ESP32. Produkcyjna gałąź HTTP
ESP32 ma oddzielny dowód PlatformIO z M6.2.

## Dowód M6.4

Diagnostic Story 1.2 przenosi `buildId` i `bundleId` zdarzenia docelowego.
Timeline, causal chain i concurrent events są ograniczone do dokładnie tej
samej pary runtime. Studio ponownie sprawdza tę spójność przed prezentacją.
Studio dołącza
findings Mentora tylko wtedy, gdy identyczny build ID występuje w Story,
aktywnym Analysis Snapshot i wyniku Mentor CLI. Następnie ogranicza findings
do stabilnych symboli tasków, pipeline'ów i komunikacji obecnych w historii.
Brak albo rozbieżność build ID pozostawia dowody Narrative dostępne, ale jawnie
wyłącza enrichment Mentora. Analiza statyczna jest opinią o tym samym systemie,
nie deklarowaną przyczyną zdarzenia runtime.

## Dowód M6.5

Studio utrzymuje dokładnie jeden zarządzany proces TraceCollector dla
zweryfikowanego latest-build. Proces jest wiązany z debug manifestem i losowym
portem loopback. Sesja przechodzi przez jawne stany lifecycle, potwierdza
`/health` i odpytuje `/events/latest?buildId=...`, dzięki czemu panel Why
automatycznie wybiera wyłącznie event aktywnego builda. Zmiana latest-build
zatrzymuje poprzedni zarządzany proces; ręcznie wskazany zewnętrzny Collector
nie staje się własnością Studio.

## Dowód M6.6

Studio projektuje zdrową zarządzaną sesję na jeden atomowy kontekst runtime:
`JUNEXIS_TRACE_URL` i `JUNEXIS_BUILD_ID`. Komendy Run C++, Go i TypeScript
przekazują parę wyłącznie procesowi potomnemu. DAP przekazuje ją jako `env`
tylko dla `launch`; `attach` nie sugeruje możliwości zmiany środowiska
istniejącego procesu. Resolver odrzuca stan inny niż `Ready`, adres
nie-loopback oraz pusty build ID. Brak wiarygodnego kontekstu jest fail-open:
artefakt uruchamia się bez Narrative.

Go i TypeScript konsumują zmienne w runtime. Hostowy proces C++ otrzymuje ten
sam kontrakt, ale fizyczny target embedded nadal wymaga definicji kompilatora
opisanych w sekcji „Protokół artefaktu”; środowisko procesu Studio nie jest
środowiskiem mikrokontrolera.

## Dowód M7.8

`deploy local` przekazuje `JUNEXIS_BUILD_ID` i `JUNEXIS_BUNDLE_ID` zarówno do
procesu Go `server`, jak i TypeScript `report`. Bezpieczny flash embedded dodaje
te same wartości jako kontrolowane `PLATFORMIO_BUILD_FLAGS`, więc firmware C++
emituje kanoniczną parę. TraceCollector 1.2 filtruje wybór i historię po obu
identyfikatorach; Story nie łączy dowodów z dwóch bundli nawet wtedy, gdy mają
ten sam Build ID. Pełny golden system E2E pozostaje osobnym zakresem M7.10.

## Dowód M6.7

Launch coordinator Studio rozróżnia trzy sytuacje:

- `Ready` — natychmiast dołącza zweryfikowany kontekst M6.6,
- `Starting` lub przełączanie buildu — czeka maksymalnie 3 sekundy,
- sesja nieaktywna, `Failed` albo timeout — uruchamia bez tracingu.

Oczekiwanie jest asynchroniczne, oparte na zmianach snapshotu i anulowalne.
Run dla C++, Go i TypeScript nie tworzy procesu potomnego przed zakończeniem
preparacji. DAP stosuje coordinator wyłącznie do `launch`; `attach` nie czeka.
Pasek Studio jawnie odróżnia przygotowanie Narrative od właściwego działania
programu. Fail-open nie oznacza cichego błędu: timeout zapisuje ostrzeżenie,
ale nie blokuje logiki użytkownika.

## Dowód M6.8

Jedyny monitor zarządzanej sesji tworzy aktywną pętlę runtime → Studio. Co
sekundę weryfikuje health i najnowszy event dokładnego build ID, a snapshot
rozróżnia:

- `Preparing` — Collector startuje albo zmienia build,
- `WaitingForEvents` — Collector jest zdrowy, lecz build jeszcze nic nie wysłał,
- `Live` — istnieje co najmniej jeden event aktywnego buildu,
- `Disconnected` — proces albo monitor utracił zdolność obserwacji.

Snapshot zawiera również deltę liczby eventów od poprzedniego odczytu i czas,
w którym Studio po raz ostatni zobaczyło nową aktywność. Panel Why automatycznie
wybiera nowy event i — przy włączonym auto-follow — pobiera jego Diagnostic
Story. Zmiana buildu unieważnia poprzedni wybór. Szybka seria snapshotów jest
wersjonowana, więc starsze oczekujące zapytanie nie zastępuje nowszego eventu.
Użytkownik może wyłączyć auto-follow bez wyłączania monitora i rejestracji
zdarzeń.

## Dowód M6.9

DAP i Narrative tworzą jeden kontrolowany przepływ diagnostyczny. Po
zatrzymaniu debuggee Studio bierze pierwszą zmapowaną klatkę, koreluje stabilny
symbol i dokładne źródło oraz automatycznie pobiera Diagnostic Story. Zapytanie
zawsze zawiera aktywny compiler build ID; opcjonalnie zawiera dokładny
`narrative_id` i event kind. TraceCollector filtruje kandydatów przed
rankingiem dowodów, dlatego event starego buildu ani innej Narrative nie może
wygrać tylko dlatego, że jest nowszy.

Wznowienie, kolejny krok, zakończenie albo nowa klatka unieważnia oczekującą
korelację. Panel Why ma niezależne `Pause follow`: zatrzymuje automatyczny wybór
Story, lecz Collector nadal rejestruje zdarzenia. `Resume` natychmiast pobiera
najnowszy event zgodny z build/Narrative/kind. Brak pasującego eventu jest
jawnym stanem, nie powodem do usunięcia filtrów lub zgadywania.

## Dowód M6.10

Historia Narrative jest nawigowana po monotonicznym `eventId`, ale granice
zbioru wyznaczają aktywne filtry M6.9. `Previous` wybiera największy pasujący
identyfikator mniejszy od kotwicy, a `Next` najmniejszy większy. Eventy innego
buildu, Narrative lub kind są pomijane deterministycznie. Brak sąsiada zwraca
jawne `404` i nie zmienia bieżącej Story.

Studio automatycznie pauzuje auto-follow przy ręcznej nawigacji, aby nowy event
runtime nie odebrał użytkownikowi wybranego punktu historii. Story można
przypiąć jako stabilny baseline. Porównanie nie interpretuje przyczyn:
raportuje zgodność build ID, dodane/usunięte elementy evidence oraz delty
timeline, causal chain, concurrent events i communications. Bieżąca korelacja
zatrzymanej klatki pozostaje osobnym kontekstem i nie jest usuwana przez
nawigację ani porównanie.

## Dowód M6.11

Nazwany bookmark jest utrwalonym snapshotem pełnej Diagnostic Story, a nie
wskaźnikiem do żywego eventu. Studio zapisuje bookmarki atomowo per workspace
w `.junexis/narrative-session.json` schema 1.0. Dzięki temu Story, evidence,
Mentor findings, komunikacje i build ID można ponownie otworzyć bez działającego
Collectora. Ponowny zapis tej samej nazwy aktualizuje stabilny slot.

Eksport sesji tworzy samowystarczalny ZIP schema 1.0. Zawiera bieżącą i
przypiętą Story, porównanie, korelację zatrzymanej klatki, bookmarki oraz
metadane filtrów. `manifest.json` publikuje SHA-256 każdego pliku dowodowego,
a stała kolejność i timestamp wpisów ZIP ułatwiają powtarzalną inspekcję.
Pakiet nie przenosi sekretów ani adresu żywego Collectora; jest archiwum
dowodów, nie ukrytą próbą wznowienia procesu runtime.

## Dowód M6.12

Import pakietu jest operacją walidacji dowodu, a nie przywróceniem żywej sesji.
Studio nie rozpakowuje ZIP do workspace. Stosuje limity liczby i rozmiaru
wpisów, odrzuca nieznane lub niebezpieczne ścieżki oraz duplikaty, a następnie
weryfikuje SHA-256 dokładnie wszystkich plików opisanych przez manifest.

Po integralności bajtowej sprawdzana jest spójność schema 1.0: identyfikatory
current/pinned muszą odpowiadać metadanym, comparison musi wskazywać te same
Story, a nazwa pliku bookmarku musi odpowiadać jego stabilnemu ID. Błąd nie
zmienia bieżącego stanu panelu. Poprawny pakiet otwiera się offline jako jawna
sesja tylko do odczytu; nie zapisuje bookmarków w `.junexis/` i nie kontaktuje
się z Collectorem. Zamknięcie importu odtwarza lokalny kontekst workspace.

## Dowód M6.13

Zweryfikowany pakiet zachowuje jawne provenance bez sugerowania zaufania do
autora: Studio pokazuje metadane zapisane przez eksport, lokalną nazwę pliku
oraz inwentarz bajtów, których SHA-256 rzeczywiście sprawdzono. Każdy wpis ma
kanoniczną ścieżkę, rozmiar i hash; „verified” dotyczy integralności i
wewnętrznej spójności kontraktu, nie prawdziwości biznesowej opisu.

Live Story otwarta przed importem może zostać zachowana jako osobny kandydat
porównania. Studio nie scala jej z pakietem. Raport `imported ↔ live` zawiera
identyfikatory obu eventów, dokładną zgodność niepustego build ID, kierunkowe
różnice evidence oraz delty timeline, causal chain, concurrent events i
communications. Wynik opisuje różnicę dwóch dowodów, a nie nową przyczynę.

## Dowód M6.14

Integralność, spójność i autentyczność są trzema osobnymi wynikami. Opcjonalny
`signature.json` zawiera ECDSA P-256/SHA-256 podpis dokładnych bajtów
`manifest.json`, publiczny klucz SPKI, deklarowaną nazwę podpisującego oraz hash
podpisanego manifestu. Podpis ma stały format IEEE P1363. Prywatny klucz PEM
pozostaje zewnętrznie zarządzany i nigdy nie trafia do bundle ani konfiguracji
Studio. Manifest podpisanego
eksportu wymaga signature entry; jego proste usunięcie daje
`InvalidSignature`, a przebudowanie manifestu tworzy inny Bundle ID.

Poprawność podpisu nie oznacza automatycznego zaufania. Studio rozróżnia
`Unsigned`, `SignatureVerified`, `TrustedSigner` i `InvalidSignature`. Zaufanie
jest lokalną decyzją przypiętą do SHA-256 fingerprintu dokładnego publicznego
klucza, nie do swobodnie deklarowanej nazwy. Trust store przechowuje wyłącznie
publiczny SPKI i może cofnąć decyzję bez modyfikowania pakietu. Podpis dowodzi
posiadania klucza; sam nie dowodzi prawdziwości biznesowej Diagnostic Story.

## Dowód M6.15

Dowód i interpretacja mają różne cykle życia. Diagnostic bundle pozostaje
niezmiennym, checksummed źródłem evidence identyfikowanym przez Bundle ID.
Recenzja jest osobnym dokumentem związanym z dokładnym Bundle ID. Rozróżnia
`Observation`, `Hypothesis`, `Decision` i `Action`, aby propozycja wyjaśnienia
nie została przedstawiona jako fakt z runtime. Wpis ma autora, czas oraz
opcjonalne odwołanie do eventu, stabilnego symbolu i pliku evidence.

Każdy zapis tworzy monotoniczną, pełną rewizję ze stanem `Open`,
`Investigating`, `Resolved` albo `Rejected`. Dwie ostatnie rewizje można
porównać kierunkowo. Opcjonalny podpis ECDSA P-256 obejmuje kanoniczny bieżący
dokument recenzji; zmiana treści lub statusu usuwa podpis z wersji roboczej.

Diagnostic case jest zewnętrznym kontenerem zawierającym oryginalny bundle,
review, metadane i własny manifest SHA-256. Import najpierw sprawdza kontener,
potem pełny kontrakt bundle, zgodność Bundle ID i rewizji oraz niezależne
statusy podpisów. Case nie scala opinii z evidence i działa bez Collectora.

## Dowód M6.16

Mentor nie jest współautorem dowodu ani autonomicznym recenzentem. Studio może
deterministycznie wyprowadzić propozycje wyłącznie z `MentorFindings` już
zapisanych w prezentowanej Diagnostic Story, gdy build ID Story i bundle są
identyczne, a `RelatedSymbolId` należy do tasku lub komunikacji tej Story.
Operacja działa offline i nie uruchamia ponownej analizy.

Finding tworzy nieobowiązujące `Observation`; opcjonalne `Explanation` tworzy
`Hypothesis`, a `RecommendedAction` tworzy `Action`. Mentor nigdy nie tworzy
`Decision`, nie zmienia statusu case i nie zapisuje rewizji. Propozycje
pozostają poza review, dopóki użytkownik nie zaakceptuje konkretnej pozycji.
Odrzucenie nie modyfikuje dokumentu.

Zaakceptowany wpis ma `Source=Mentor` oraz stabilny reference obejmujący kod
reguły, rodzaj propozycji i build ID. Zachowuje również event, symbol i
zweryfikowaną ścieżkę Story w bundle. Przyjęcie jest edycją wersji roboczej,
więc usuwa jej podpis. Człowiek nadal podaje autora i jawnie zapisuje następną
rewizję; tylko człowiek może sformułować Decision.
