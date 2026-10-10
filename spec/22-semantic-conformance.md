# M8 — Semantic Conformance

> **Status:** EXPERIMENTAL / VERIFIED dla M8.1–M8.8
> **Kierunek:** D16 — wspólna semantyka przed backendami

M7 udowodniło, że jeden system można zbudować, podpisać, wdrożyć i obserwować.
M8 ma odpowiedzieć na trudniejsze pytanie:

> Czy ten sam program Junexis znaczy to samo, niezależnie od tego, czy jego
> fragment wykonuje wygenerowany C++, Go czy TypeScript?

Backendy są implementacjami semantyki Junexis, a nie jej źródłem. Zielony
native build potwierdza poprawność składni języka hosta. Nie potwierdza jeszcze
zgodności zachowania.

## Cel etapu

M8 ustanawia wykonywalny, wersjonowany kontrakt semantyczny dla reprezentatywnego
podzbioru języka i dowodzi go na wszystkich trzech backendach bootstrapowych.
Kontrakt obejmuje wartości, sterowanie, lifecycle tasków, stan, czas,
komunikację oraz obserwowalne skutki na granicach systemu.

Nie chodzi o wymuszenie identycznej implementacji. C++ może używać FreeRTOS,
Go goroutines, a TypeScript event loop. Różne mechanizmy muszą jednak dawać
wynik zgodny z tym samym kontraktem Junexis.

## Poziomy dowodu

| Poziom | Znaczenie |
|---|---|
| S0 — declared | Semantyka jest opisana nazwą, wejściem, oczekiwanym wynikiem i granicami. |
| S1 — lowered | Typed Semantic IR zachowuje wymagane znaczenie przed platform lowering. |
| S2 — executable | Wygenerowany artefakt przechodzi natywny scenariusz zachowania. |
| S3 — equivalent | C++, Go i TypeScript zwracają ten sam znormalizowany wynik scenariusza. |
| S4 — distributed | Zgodność jest zachowana po przekroczeniu granicy targetu i wire contract. |

Raport nie może podnosić statusu scenariusza wyżej niż najniższy rzeczywiście
udowodniony poziom. Brak toolchainu jest wynikiem `not-run`, a nie `passed`.

## Jednostka zgodności

Scenariusz conformance powinien być danymi, nie nazwą testu zaszytą wyłącznie
w kodzie. Minimalny kontrakt opisuje:

- stabilny identyfikator i wersję semantyki;
- źródło `.jnx` albo jego kanoniczny fragment;
- wymagane targety i poziom dowodu;
- znormalizowane wejścia, wynik, kolejność i obserwowalne skutki;
- jawne ograniczenia, np. brak gwarancji czasu ściennego;
- wynik każdego backendu wraz z toolchainem;
- Build ID scenariusza i hash kontraktu;
- odwołania do Narrative evidence, jeśli scenariusz przekracza granice systemu.

Pierwszy format raportu będzie nosił nazwę
`junexis.semantic-conformance/0.1`.

## Zakres semantyczny M8

1. Wartości, typy i podstawowe wyrażenia.
2. `if`, iteracja oraz kontrolowane zakończenie.
3. Wywołanie tasku, stan, inicjalizacja i lifecycle.
4. Jednostki czasu oraz semantyka `every` bez obietnicy dokładności zegara.
5. `chan<T>` — FIFO, konsumpcja i backpressure producenta.
6. `queue<T>` — FIFO, at-least-once, ACK/NACK i retencja do ACK.
7. `stream<T>` — latest-wins, fanout i brak history replay.
8. Typowane `extern` jako jawna, wstrzykiwalna granica efektu.
9. Wersjonowany wire contract na reprezentatywnej granicy targetów.
10. Canonical Narrative evidence dla zachowania rozproszonego.

## Granica Typed Semantic IR

M8 nie wymaga od razu kompletnego nowego IR całego języka. Wprowadza minimalny
Typed Semantic IR potrzebny przez scenariusze conformance. Ma on jawnie
reprezentować co najmniej:

- typ i normalizację wartości;
- efekt czysty, stanowy, komunikacyjny lub zewnętrzny;
- operację komunikacyjną wraz z kontraktem delivery/retention/ordering;
- lifecycle oraz granicę anulowania;
- pochodzenie source → semantic operation.

Platform lowering może wybrać mechanizm wykonania dopiero po utrwaleniu tych
właściwości. Nie może dopowiadać semantyki na podstawie języka hosta.

## Kolejność realizacji

| Etap | Wynik |
|---|---|
| M8.1 | Wersjonowany ledger scenariuszy i raport `junexis.semantic-conformance/0.1`. |
| M8.2 | Minimalny Typed Semantic IR 0.1 oraz source mapping dla badanego podzbioru. |
| M8.3 | Zgodność wartości, typów, wyrażeń i control flow. |
| M8.4 | Zgodność task lifecycle, stanu i czasu. |
| M8.5 | Wykonywalna zgodność `chan<T>` na C++/Go/TypeScript. |
| M8.6 | Wykonywalna zgodność `queue<T>` wraz z ACK/NACK. |
| M8.7 | Wykonywalna zgodność `stream<T>` i fanout. |
| M8.8 | Zgodność `extern`, wire contract i reprezentatywnej granicy targetów. |
| M8.9 | CLI, Studio i Mentor pokazują status oraz luki conformance bez udawania błędu kompilacji. |
| M8.10 | ✅ Golden Semantic E2E z raportem S3/S4 związanym z Build ID i Bundle ID. |

## Istniejący punkt startowy

M8 nie zaczyna od zera. `CommunicationRuntimeContractCatalog` opisuje obecnie
delivery, backpressure, retention i ordering dla `chan`, `queue` i `stream`.
`GeneratedRuntimeConformanceTests` uruchamia natywne fragmenty C++, Go i
TypeScript. Są to ważne dowody cząstkowe, ale nie tworzą jeszcze jednego
wersjonowanego scenariusza ani porównywalnego raportu S3.

M8.1 ma skonsolidować te dowody bez przepisywania działających runtime'ów.

## Dowód M8.1

Kanoniczny ledger znajduje się w `conformance/semantic-scenarios.json`.
`scripts/test-semantic-conformance.ps1` w repozytorium platformy uruchamia
walidację ledgeru oraz testy `RuntimeConformance02`, po czym tworzy raport
`junexis.semantic-conformance/0.1`.

Pierwszy zweryfikowany wynik jest świadomie częściowy:

| Scenariusz | C++ | Go | TypeScript | Wspólny poziom |
|---|---:|---:|---:|---:|
| `chan<T>` FIFO + backpressure | S2 | S2 | S1 | S1 |
| `queue<T>` at-least-once + ACK/NACK | S2 | S2 | S2 | S2 |
| `stream<T>` latest-wins | S2 | S2 | S2 | S2 |

Raport kończy się technicznym `PASS`, ponieważ ledger jest prawidłowy i każdy
zadeklarowany dowód przechodzi. Pole `conformanceStatus` pozostaje `partial`,
`targetSatisfiedCount` wynosi `0/3`, a trzy luki do S3 są jawne. M8.1 nie
zamienia dowodów S1/S2 w deklarację równoważności S3.

## Typed Semantic IR 0.1 — M8.2

`TypedSemanticProjector` tworzy z istniejącego Structural IR osobny kontrakt
`junexis.typed-semantic-ir/0.1`. Projekcja nie zawiera nazw ani konstrukcji
specyficznych dla C++, Go lub TypeScript. Dla badanego podzbioru utrwala:

- znormalizowane typy wejść, wyjścia, stanu, kolekcji, map i unionów;
- lifecycle tasku: one-shot, periodic, looping, hook-driven albo mixed;
- spłaszczone operacje ze stabilnym ID oraz source origin;
- główną kategorię efektu: pure, state, control, time, communication, external;
- `send`, `receive`, ACK/NACK i ich kanoniczny kontrakt
  `CommunicationRuntimeContractCatalog`;
- jawne wywołania `extern` jako efekt external, oddzielony od czystej ewaluacji.

Projekcja jest świadomie minimalna. Nie zastępuje jeszcze całego Structural IR,
nie wykonuje kompletnego type inference i nie jest jeszcze publikowana jako
artefakt System Bundle. Platform lowering nadal korzysta ze Structural IR;
przenoszenie kolejnych decyzji za granicę Typed Semantic IR należy do M8.3–M8.8.

Dowód M8.2 obejmuje sześć testów projekcji oraz pełny zestaw 242 testów
`JunexisLang.Compiler.Tests`, wszystkie zielone bez ostrzeżeń kompilatora.

## Wartości, wyrażenia i control flow — M8.3

Ledger używa od M8.3 nadrzędnego kontraktu `junexis.semantic-runtime/0.1`.
Pierwszy scenariusz równoważności S3 wykonuje ten sam program na wygenerowanych
artefaktach C++, Go i TypeScript. Program obejmuje:

- wartości `Int`, `Float`, `Text` i `Bool`;
- arytmetykę, porównania i operator logiczny `and`;
- priorytet operatorów w złożonym wyrażeniu;
- gałęzie `if`, `else-if` i `else`;
- trzy różne ścieżki całkowitoliczbowe, dwie tekstowe i wynik zmiennoprzecinkowy.

Każdy backend publikuje wynik w tym samym canonical JSON. Test najpierw
porównuje C++, Go i TypeScript między sobą, a następnie porównuje wynik z
oczekiwanym znaczeniem programu:

```json
{"evaluate":[13,12,-5],"choose":["positive","other"],"ratio":2.5}
```

Scenariusz `values.scalar-expressions-control-flow` osiąga S3. Cały raport
pozostaje `partial`: `1/4` scenariuszy spełnia cel, a trzy scenariusze
komunikacyjne pozostają na S1/S2 do etapów M8.5–M8.7.

## Lifecycle tasku, stan i czas — M8.4

Kontrakt `junexis.task-lifecycle/0.1` oddziela znaczenie programu od schedulera
hosta. Dla ograniczonego scenariusza okresowego utrwala:

- pojedyncze `start` przed pierwszym wykonaniem i pojedyncze `stop` po
  zaobserwowaniu anulowania;
- anulowanie pomiędzy zakończonymi tickami, bez przerywania rozpoczętej
  aktualizacji stanu;
- inicjalizację stanu raz na instancję tasku i zachowanie wartości pomiędzy
  kolejnymi wywołaniami;
- `Duration` normalizowane do milisekund;
- pierwszy tick `every` po upływie interwału, nie natychmiast przy starcie.

Scenariusz używa kontrolowanego czasu logicznego. Nie mierzy czasu ściennego i
nie obiecuje precyzji, jitteru ani klasy real-time. Natywne artefakty C++, Go i
TypeScript zwracają identyczny canonical JSON:

```json
{"lifecycle":["start","tick","tick","tick","stop"],"state":[12,15,19],"intervalMs":250,"timing":"logical-after-interval"}
```

W ramach naprawy conformance zwykłe ciało tasku TypeScript zostało podłączone
do istniejącego load/save state z zapisem w `finally`, a lowering C++ `every`
został wyrównany do Go i TypeScript: oczekiwanie następuje przed pierwszym
tickiem. Scenariusz osiąga S3. Raport pozostaje `partial` z wynikiem `2/5`, bo
trzy kontrakty komunikacyjne oczekują na M8.5–M8.7.

## `chan<T>` — M8.5

`chan<T>` ma jeden wspólny kontrakt: ograniczone FIFO, producent czeka przy
pełnej pojemności, a wartość pozostaje zachowana do konsumpcji. M8.5 usunęło
dwie rozbieżności ukryte przez wcześniejszy zielony build:

- runtime C++ używa teraz współdzielonego, kopiowalnego stanu kanału z
  pojemnością 1, mutexem i condition variable zamiast nieograniczonego deque;
- TypeScript używa asynchronicznego `Channel<T>` zamiast `buffer.push`, a
  wygenerowany producent działa współbieżnie, czeka na `send` i zwraca uchwyt
  kanału konsumentowi.

Go zachowuje natywny ograniczony kanał. Jeden test uruchamia wszystkie trzy
runtime'y i porównuje canonical JSON:

```json
{"fifo":[1,2],"producerWaited":true,"retention":"until-consumed"}
```

Scenariusz `communication.chan.fifo-backpressure` osiąga S3. Zbiorczy raport
pozostaje `partial` z wynikiem `3/5`; otwarte są `queue<T>` i `stream<T>`.

## `queue<T>` z ACK/NACK — M8.6

Scenariusz `queue<T>` utrwala at-least-once jako obserwowalny protokół, a nie
samą nazwę gwarancji:

- `recv` zwraca tę samą wiadomość i stabilny Message ID aż do ACK;
- NACK zachowuje wiadomość do ponownego dostarczenia;
- ACK nieznanego ID nie zmienia kolejki;
- prawidłowy ACK usuwa wyłącznie potwierdzoną wiadomość;
- kolejny `recv` odsłania następny element FIFO z kolejnym Message ID.

C++ otrzymał brakujący delivery frame i ukryte powiązanie `recv` → Message ID
dla instrukcji `ack`/`nack`. Składnia pozostaje eksperymentalna, ale lowering
C++/Go/TypeScript jest wykonywalny. Jeden natywny test porównuje canonical JSON
wszystkich backendów. Scenariusz osiąga S3, a raport `4/5`; otwarty pozostaje
`stream<T>` w M8.7.

## `stream<T>` z fanout — M8.7

`stream<T>` jest relacją latest-wins z niezależnym kursorem każdej
subskrypcji. Publikacja nie czeka na odbiorców i zastępuje poprzednią wartość.
Subskrypcja utworzona po publikacji nie odtwarza historii, a ponowne `poll`
bez nowej publikacji nie zwraca duplikatu.

Runtime C++, Go i TypeScript udostępniają jawne `subscribe` oraz `poll`.
Jeden natywny scenariusz potwierdza koalescencję, brak replay, brak duplikatu
i fanout do trzech niezależnych odbiorców. Wszystkie backendy zwracają ten sam
canonical JSON:

```json
{"coalesced":[2,2],"noDuplicate":true,"noHistoryReplay":true,"fanout":[4,4,4],"latest":4}
```

Scenariusz osiąga S3. Ledger ma teraz `5/5` scenariuszy na poziomie docelowym;
zamyka to macierz rdzenia semantycznego, ale nie cały M8 — pozostają granice
`extern` i wire, prezentacja statusu w narzędziach oraz golden E2E.

## `extern`, wire contract i granica targetów — M8.8

M8.8 łączy wcześniejsze osobne dowody w jeden scenariusz rozproszony. Typowany,
wstrzykiwalny `extern task` wykonuje się po stronie `server`; jego wynik jest
publikowany przez `stream<Text>`, serializowany w Go i odczytywany przez
wygenerowany TypeScript targetu `report`.

Kontrakt `junexis-wire/0.2` nie jest już powielonym literałem backendów.
`WireRuntimeContractCatalog` w IR definiuje wersję, JSON, media type, nagłówek
wersji oraz pola koperty. Distributed Topology IR i oba generatory korzystają
z tego samego źródła. Odbiorca odrzuca kopertę z nieobsługiwaną wersją.

Typed Semantic IR zachowuje zagnieżdżone wywołanie `extern` jako osobny efekt
`external` przed efektem `communication`. Binding powstaje wyłącznie na
targetcie właściciela i nie przecieka do kodu raportu. Natywny scenariusz
Go server → TypeScript report zwraca canonical JSON:

```json
{"extern":"extern:hello-l4","wire":"junexis-wire/0.2","boundary":"server->report","rejectsUnknownVersion":true}
```

Scenariusz `boundary.extern-wire.server-report` osiąga S4. Ledger obsługuje
indywidualny `targetLevel`: pięć scenariuszy nadal wymaga S3, granica
rozproszona wymaga S4. Raport osiąga `6/6` bez podnoszenia lokalnych
scenariuszy do poziomu rozproszonego.

## Status w narzędziach — M8.9

CLI udostępnia `junexis conformance <raport> [--json]`. Pokazuje wynik i luki,
ale nie zmienia sukcesu kompilacji: poprawny raport `partial` jest wynikiem
nieblokującym i kończy komendę kodem `0`.

Mentor CLI 1.2 przyjmuje opcjonalne `--conformance <raport>` i publikuje osobną
opinię `conformance` z licznikami, listą luk i `blocking: false`. Nie zamienia
luki conformance w finding. Studio wykrywa raport `semantic-conformance.json`
przy snapshotcie, w `out/`, `.junexis/` lub katalogu projektu i pokazuje go w
`Analysis → Mentor → Conformance`.

Raport 0.1 pozostaje wersjonowanym dowodem projektu i jest prezentowany jako
`UNBOUND`. Nie wolno interpretować go jako dowodu konkretnego buildu.

## Tożsamość dowodu i Golden Semantic E2E — M8.10

Raport `junexis.semantic-conformance/0.2` zawiera `identity.status = bound`,
Build ID, Bundle ID, hash ledgeru oraz `bindingSha256`. Binding jest SHA-256 z
kanonicznego UTF-8:

```text
<buildId>\n<bundleId>\n<ledgerSha256>
```

CLI przelicza binding i odrzuca niespójność. Mentor wykonuje tę samą walidację
oraz wymaga, aby Build ID raportu był identyczny z Build ID analizowanego
snapshotu. Opinia pozostaje `blocking: false`: związanie wzmacnia pochodzenie
dowodu, lecz nie zamienia Mentora w kompilator ani quality gate.

Studio prezentuje raport 0.2 jako `BOUND build <skrót> / bundle <skrót>`.
Raport 0.1 jest jawnie `UNBOUND`. Golden E2E tworzy podpisany bundle dla C++, Go
i TypeScript, generuje związany raport, przepuszcza go przez CLI i Mentora,
odrzuca kontrolowaną zmianę Bundle ID, a następnie zachowuje tę samą parę
identyfikatorów w deployment i obserwowalności.

Tym samym M8 jest zamknięty dla zdefiniowanego w ledgerze podzbioru S3/S4. Nie
jest to deklaracja stabilności 1.0 ani pełnej semantyki wszystkich przyszłych
konstrukcji Junexis.

## Poza M8

- Junexis bytecode, VM, pełne Native AOT i WebAssembly;
- `resource`, storage, filesystem i typowane sekwencyjne I/O z D17;
- produkcyjny scheduler czasu rzeczywistego;
- pełna macierz wszystkich protokołów i platform sprzętowych;
- debugger krokowy;
- fizyczny smoke ESP32, który pozostaje otwartą granicą M7.7;
- deklaracja stabilności 1.0.

Te elementy mogą korzystać z kontraktów M8 później. Nie są jednak warunkiem
uczciwego zamknięcia Semantic Conformance dla wybranego podzbioru.
