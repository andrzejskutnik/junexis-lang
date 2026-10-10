# Compiler and runtime conformance baseline (M5.5)

Status: **0.2 Experimental — wąski baseline przyjęty 2026-06-24**

M5.5 jest obowiązkową bramką między toolingiem M5 a Narrative i Embedded
Toolchain M6. Jej celem nie jest dodanie nowej składni, lecz udowodnienie, że
zadeklarowany zakres 0.2 ma spójny kompilator, LSP i warstwę wykonawczą.

## Co znaczy „działa dla 0.2”

Funkcja należy do działającego baseline dopiero wtedy, gdy:

1. poprawny program przechodzi parser, semantykę, IR i właściwy lowering;
2. błędny program kończy się kontrolowaną diagnostyką;
3. backend tworzy kompletny artefakt i przechodzi natywny toolchain;
4. uruchomiony artefakt zachowuje kontrakt runtime, a nie tylko się kompiluje;
5. LSP publikuje tę samą ocenę poprawności co CLI;
6. dowód jest powtarzalnym testem, nie ręczną obserwacją.

M5.5 nie wymaga implementowania funkcji przypisanych dopiero do M6. Wymaga
jednak, aby wszystkie fundamenty używane przez M6 były jawne, testowalne i nie
miały sprzecznych implementacji.

## Rodziny runtime wymagające rozróżnienia

Aktualny system zawiera kilka niezależnych warstw nazywanych runtime:

| Rodzina | Lokalizacja | Rola 0.2 |
|---------|------------|----------|
| Go bootstrap | generowany `server/main.go` | aktywny runtime artefaktów `@server` |
| TypeScript bootstrap | generowany `report/runtime.ts` | aktywny runtime artefaktów `@report` |
| C++ bootstrap | generowany `embedded/junexis/runtime.h` | aktywny runtime artefaktów `@embedded` |
| .NET reference runtime | repo `junexis-runtime` | implementacja referencyjna komunikacji, execution i resilience |
| embedded C ABI | `junexis-runtime/runtime-embedded` | przenośny runtime kanałów i schedulera |
| embedded Rust ABI | `junexis-runtime/runtime-embedded-rs` | eksperymentalna implementacja tego samego ABI |

Build każdej rodziny osobno nie oznacza ich zgodności. W stanie wejściowym M5.5
codegen emituje runtime’y bootstrapowe i nie linkuje bibliotek z repo
`junexis-runtime`.

## Minimalna macierz semantyki wykonawczej

| Kontrakt | Wymagane zachowanie baseline 0.2 |
|----------|----------------------------------|
| `chan<T>` | point-to-point, kolejność FIFO, jawne zamknięcie i bounded backpressure |
| `queue<T>` | identyfikator wiadomości, receive bez utraty przed ACK, ACK usuwa, NACK zachowuje lub kieruje do retry/DLQ |
| `stream<T>` | latest-wins; wolny konsument może pominąć wartości historyczne |
| task | przewidywalny start, zakończenie i propagacja błędu |
| time | spójne `delay`/`every` bez zakleszczenia hostowego runtime |
| wire | wersjonowana koperta `junexis-wire/0.2` i kontrolowane odrzucenie niezgodnej wersji |
| extern | jawny binding albo kontrolowany błąd wykonania |
| telemetry | instrumentacja nie może zmieniać poprawności ani powodować race condition |

Trwałość `queue<T>` musi być zapewniona przez stronę będącą właścicielem kolejki.
Adapter kliencki może być nietrwały, jeżeli protokół ACK pozostawia wiadomość u
właściciela do czasu potwierdzenia.

## Poziomy dowodu M5.5

1. **R0 — inventory:** każda aktywna implementacja i jej właściciel są znane.
2. **R1 — deterministic build:** solution i natywne runtime’y budują się bez
   losowych wyników testów.
3. **R2 — local conformance:** każda aktywna implementacja przechodzi wspólną
   macierz `chan`/`queue`/`stream`, czasu, błędów i lifecycle.
4. **R3 — compiler/runtime contract:** artefakt wygenerowany przez kompilator
   wykonuje tę samą macierz, bez ręcznego przepisywania runtime.
5. **R4 — vertical system:** reprezentatywny pipeline 0.2 uruchamia co najmniej
   dwa backendy i przechodzi przez wersjonowany wire contract.

M6 może się rozpocząć dopiero po osiągnięciu R4 dla uzgodnionego wąskiego
baseline. Pozostałe backendy mogą mieć status eksperymentalny tylko wtedy, gdy
nie są przedstawiane jako aktywny, działający target 0.2.

## Stan wejściowy audytu

- kompilator i LSP: 418/418 testów; 13/13 głównych przykładów przechodzi `check`;
- codegen: natywne L2 istnieje dla trzech targetów, ale szerokie L3 nie istnieje;
- runtime .NET: rozbudowany zestaw testów, początkowo niestabilny przez telemetry,
  arbitralną asercję rozmiaru WAL i niepełne zwalnianie `HttpListener`;
- embedded C: CMake zawierał błędne ścieżki, a testy hostowe blokowały się bez
  źródła ticków;
- embedded Rust: kod istnieje, lecz bieżące środowisko nie ma toolchainu Cargo;
- największa luka: brak wspólnej wykonywalnej macierzy dla runtime’ów
  bootstrapowych i implementacji z `junexis-runtime`.

Ten stan był punktem startowym pracy, nie akceptacją M5.5.

## Wynik audytu i przyjęty zakres

M5.5 osiąga R4 dla aktywnych artefaktów bootstrapowych 0.2:

- pełny kompilator, CLI i LSP przechodzą 423 testy;
- wszystkie 13 głównych przykładów przechodzi `junexis check`; jedynym
  oczekiwanym ostrzeżeniem jest logiczny cykl zadeklarowany w przykładzie 02;
- generowane C++, Go i TypeScript mają natywne, wykonywalne testy rozróżniające
  FIFO `chan`, ACK/NACK `queue` i latest-wins `stream`;
- wygenerowany Go i TypeScript przechodzą pionowy test procesu przez
  `junexis-wire/0.2`;
- referencyjny runtime .NET przechodzi dwukrotnie 540/540 testów, w tym testy
  współbieżności, telemetry, trwałości WAL, retry i DLQ;
- zapis WAL jest wykonywany przed zmianą stanu pamięci, jawnie odrzuca
  uszkodzone kompletne rekordy i toleruje wyłącznie urwany rekord końcowy;
- embedded C buduje się przez CMake i przechodzi 2/2 testy CTest dla kanału,
  backpressure, timeoutów, zamknięcia oraz schedulera;
- audyt NuGet nie wykazuje znanych podatnych zależności.

Pełną bramkę kompilatora należy uruchamiać sekwencyjnie:

```powershell
dotnet test compiler\JunexisLang.Compiler.slnx --no-restore -m:1
```

Testy codegen same uruchamiają Go, npm, Node i C++; równoległe uruchomienie
projektów testowych może powodować wyścigi o ich współdzielone cache i nie jest
miarodajną bramką deterministyczności.

Przyjęcie M5.5 nie oznacza jeszcze jednego uniwersalnego runtime. Bootstrapowe
runtime'y pozostają częścią generowanych artefaktów, .NET jest implementacją
referencyjną, a C ABI nie jest jeszcze linkowane przez codegen C++.

Poza aktywną bramką pozostają:

- embedded Rust ABI — eksperymentalny; brak Cargo w środowisku audytu;
- NativeAOT test .NET — kompilacja dociera do linkowania, lecz środowisko nie ma
  workloadu Visual C++/linkera wymaganego przez NativeAOT;
- ujednolicenie ABI wszystkich backendów — kierunek po baseline 0.2, a nie
  warunek rozpoczęcia M6.
