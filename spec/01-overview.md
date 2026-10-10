# Junexis Language — Przegląd i zakres 0.2 Experimental

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

Nadrzędne znaczenie statusów opisuje [`STATUS.md`](./STATUS.md). Cała
specyfikacja 0.2 jest eksperymentalna; etykieta `CORE` nie oznacza jeszcze
stabilności ani kompletnej implementacji.

---

## Czym jest Junexis Lang

Junexis Lang (`.jnx`) to deklaratywny język dla rozproszonych systemów, w którym **komunikacja jest obywatelem pierwszej klasy**, a nie abstrakcją biblioteczną.

Zamiast pisać imperatywny kod integracyjny, developer opisuje *co* system robi i
*where* (na jakim device/platformie). Projektowany kompilator generuje kod dla
wielu platform i adaptery protokołów; aktualny poziom dowodu backendów opisuje
[`11-code-generation.md`](./11-code-generation.md).

```junexis
task TempSensor() @embedded -> chan<SensorData>
task DataProcessor(input: chan<SensorData>) @server -> chan<Alert>

deployment Home {
    TempSensor@esp32 -> DataProcessor@aws
}
```

To jest docelowa obietnica języka: C++ dla ESP32, backend serwerowy i adapter
wynikający z protokołu. W 0.2 sam zapis nie stanowi jeszcze dowodu działającego
MQTT ani semantycznego loweringu deploymentu.

---

## Trzy założenia fundamentalne

1. **Communication-First** — `chan<T>`, `queue<T>`, `stream<T>` to typy pierwszoklasowe, nie typy biblioteczne.

2. **Platform-Aware** — każdy `task` nosi adnotację docelowej platformy. Kompilator generuje kod dla każdej platformy oddzielnie.

3. **Progressive Disclosure** — prosty system działa z minimum wiedzy (Level 1: sam deployment); zaawansowane opcje dostępne, gdy potrzebne (Level 2: `device` + `config` + `protocol { }`; Level 3: pełna konfiguracja).

---

## Zakres 0.2

### 🧭 Projektowany rdzeń 0.2 — CORE

| Obszar | Co |
|--------|----|
| Typy komunikacyjne | `chan<T>`, `queue<T>`, `stream<T>` |
| Deklaracje ustrukturyzowane | `task`, `struct`, `enum`, `fn`, `pipeline`, `device`, `import`, `extern task` |
| Manifest zależności (D13) | `junexis.toml` — pakiety @server/@report; poza .jnx |
| Deklaracje metadanych (opaque) | `narrative`, `deployment`, `supervisor`, `circuit_breaker`, `quirks` |
| System typów — high-level | `Int`, `Float`, `Bool`, `Text`, `Symbol`, `Time`, `Duration` |
| System typów — sized | `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64` |
| Typy złożone | `Map<K,V>`, `[T]` (list), `T\|U` (union), generyki `X<T>` |
| Platformy | `@embedded`, `@server`, `@report` (PRIMARY — CORE) |
| Protokoły (D7) | deklaracja `protocol <name> { }` — top-level binding (D7) |
| System modułów (D12) | `import m::path`, `import m::path as ns`, `import m::path { X, Y }` |
| Przepływ sterowania | `if/else`, `for` (iteracja / warunkowa / nieskończona), `match`, `every`, `delay`, `return` |
| Komunikacja (statement) | `send`, `recv`, `respond` |
| Lifecycle hooks | `on <event>` — keyword `ON` + identyfikator zdarzenia; np. `on start`, `on connect`, `on reconnect`, `on disconnect` |
| Literały | integer, float, string (`"..."`, `'...'`), raw string (`@"..."`), multiline string (`"""`), bool, duration (`5s`, `100ms`), memory (`32KB`, `512MB`) |
| Operatory | arytmetyczne (`+ - * / %`), przypisanie, porównanie, logiczne, specjalne (`->`, `::`, `.`, `..`, `@`, `?`, `\|`, `&`) |

### ⚗️ W zakresie 0.2 — EXPERIMENTAL

Deklaracje opaque (D3: intencjonalne — parser zbiera balanced braces bez analizy treści) i dodatkowe platformy. Poprawne składniowo, semantyka egzekwowana tylko przez docs, nie przez kompilator.

| Element | Uwaga |
|---------|-------|
| `narrative { ... }` | Opaque metadata block |
| `deployment { ... }` | Opaque deployment block |
| `supervisor { ... }` | Opaque supervision block |
| `circuit_breaker { ... }` | Opaque resilience block |
| `quirks { ... }` | Opaque hardware-workaround block |
| `extern task ...` | FFI bridge — sygnaura w .jnx, implementacja w pliku natywnym (D13) |
| `storage { entity ... query ... }` | M13.2–M13.13: neutralny Query Plan, trzy providery, bezpieczny pushdown, typowane wywołanie z tasku oraz workspace IDE |
| `@edge`, `@cloud_server`, `@mobile`, `@desktop`, `@web` | Extended platform targets |
| `@microcontroller`, `@fog_node`, `@data_center` | Extended platform targets |

### 🔒 RESERVED — zarezerwowane, bez reguł parsera w 0.2

Tokeny zdefiniowane w lekserze, lecz bez odpowiadających reguł parsera. Kompilator rozpoznaje je jako słowa kluczowe (nie są identyfikatorami użytkownika), ale nie potrafi ich sparsować.

| Token | Rationale |
|-------|-----------|
| `retry` | Implementacja w runtime, reguła parsera planowana 0.3 |
| `fallback` | j.w. |
| `bulkhead` | j.w. |

### 🚫 POZA zakresem 0.2

Poniższe elementy **nie istnieją** w gramatyce 0.2. Nie są placeholderami — są świadomie wykluczone. Mogą wejść w późniejszych wersjach po analizie use-case.

| Element | Dlaczego poza zakresem |
|---------|------------------------|
| `resource`, `Source<T>`, `Sink<T>` | Kierunek D17 jest udokumentowany, ale nie ma jeszcze kanonicznej składni ani ścieżki implementacyjnej. |
| `null`, `undefined` | Null safety przez `T\|None` i optional types; explicit null dodaje złożoność |
| Literały hex (`0x2A`), octal (`0o52`), binary (`0b1010`) | Nie istnieje use-case w języku wyższego poziomu; `u8`, `u32` obsługują to przez wartości dziesiętne |
| Interpolated strings (`f"..."`, `${expr}`) | Wymaga analizatora wyrażeń wewnątrz leksera; odroczone |
| Single-quoted multiline (`'''...'''`) | Triple-quoted `"""..."""` wystarcza |
| Char literals (`'a'`) | `Text` obsługuje znaki; osobny typ `Char` nie ma uzasadnienia w języku systemów |
| Operator bitowy (`^`, `~`, `<<`, `>>`, `&=`, `\|=` itd.) | Embedded use-case: użyj `Int` z operacją w natywnym bloku `quirks` |
| Pipeline operator (`\|>`) | Składnia pipeline działa przez `pipeline { use A -> B }` |
| Spaceship operator (`<=>`) | Brak precedensu — nie dodajemy bez konkretnego use-case |
| Optional chaining (`?.`) | Brak null w 0.2 |
| Elvis operator (`?:`) | j.w. |
| Left arrow assignment (`<-`) | Niejednoznaczne z komunikacją; `recv` jest czytelniejsze |
| Fat arrow (`=>`) | `->` obsługuje return type annotation; `=>` byłoby aliasem |
| `select { }` statement | Odroczone; `match` + `recv` pokrywają use-cases |
| `when` block | j.w. |
| `break`, `continue` | Przepływ sterowania przez `return` i `for`; dodanie break/continue po analizie |
| `timeout` jako statement | Resilience przez `circuit_breaker` (opaque) i `@protocol` config |
| `supervise` keyword (statement) | Supervision przez `supervisor` deklarację (opaque) |
| `backpressure`, `select` keywords | Phantom z old INVENTORY — nie istniały w gramatyce |
| Phantom platform annotations | `@from`, `@to`, `@cloud`, `@report_frontend`, `@embedded_device`, `@consensus_leader`, `@backup_node`, `@remote_cluster`, `@edge_gateway` — nie istniały, nie dodajemy |
| `export` keyword | Wszystkie top-level deklaracje są public; brak potrzeby explicit export w 0.2 (D12) |
| `import "./path.jnx"` (ścieżki pliku) | 0.2 używa wyłącznie `::` module paths; file-based odroczone do późniejszej wersji (D12) |

---

## Poziomy dojrzałości (jak czytać spec)

W plikach specyfikacji każdy element nosi oznaczenie:

- **CORE** — element należy do projektowanego rdzenia 0.2; nie oznacza to pełnej implementacji ani kompatybilności.
- **EXPERIMENTAL** — kierunek jest badany; składnia i semantyka mogą się zmienić.
- **RESERVED** — token leksykonu, brak reguły parsera. Użycie jako identyfikatora użytkownika jest błędem składniowym.
- **NOT IN 0.2** — element nie istnieje w języku. Jeśli chcesz napisać to samo, patrz sekcja "Poza zakresem" powyżej po alternatywę.

---

## Pliki specyfikacji

| Plik | Temat |
|------|-------|
| [`STATUS.md`](./STATUS.md) | Oficjalna wersja i poziomy dojrzałości |
| [`00-philosophy.md`](./00-philosophy.md) | Filozofia języka i podział odpowiedzialności |
| [`02-lexical.md`](./02-lexical.md) | Tokeny, słowa kluczowe, literały |
| [`03-types.md`](./03-types.md) | System typów |
| [`04-declarations.md`](./04-declarations.md) | Deklaracje top-level |
| [`05-communication.md`](./05-communication.md) | Typy i operacje komunikacyjne |
| [`06-control-flow.md`](./06-control-flow.md) | Przepływ sterowania, wyrażenia |
| [`07-platform.md`](./07-platform.md) | Adnotacje platformy |
| [`08-protocols.md`](./08-protocols.md) | Protokoły — deklaracja `protocol` (D7) |
| [`09-decisions.md`](./09-decisions.md) | Rejestr decyzji projektowych |
| [`10-resources-and-io.md`](./10-resources-and-io.md) | Projekt D17: resources, storage i typowane I/O |
| [`25-storage-contract.md`](./25-storage-contract.md) | Kontrakt `junexis.storage/0.1` niezależny od providera |
| [`26-sqlite-provider.md`](./26-sqlite-provider.md) | Referencyjny provider SQLite bez CGO |
| [`27-memory-storage-provider.md`](./27-memory-storage-provider.md) | Zgodny provider pamięciowy i wspólna granica domenowa |
| [`37-generated-storage-runtime-client.md`](./37-generated-storage-runtime-client.md) | M12.7: neutralny klient Runtime i wspólna bramka providerów |
| [`38-storage-launcher-and-bundle.md`](./38-storage-launcher-and-bundle.md) | M12.8: zweryfikowany launcher i provider w System Bundle |
| [`39-storage-laboratory.md`](./39-storage-laboratory.md) | M12.9: trzy progresywne ścieżki Laboratorium Storage |
| [`40-provider-repository-boundary.md`](./40-provider-repository-boundary.md) | M12.10: providery poza repo Runtime i osobna bramka wydawnicza |
| [`41-storage-runtime-sdk.md`](./41-storage-runtime-sdk.md) | M12.11: wersjonowane paczki SDK bez zależności źródłowej między repo |
| [`42-storage-query-plan-ir.md`](./42-storage-query-plan-ir.md) | M13.1: abstrakcyjny model danych i przenośny Query Plan IR |
| [`43-storage-source-projection.md`](./43-storage-source-projection.md) | M13.2: deklaracja `storage` i projekcja źródła do kontraktów M13.1 |
| [`44-storage-runtime-query-lowering.md`](./44-storage-runtime-query-lowering.md) | M13.3: neutralne wywołanie `query.plan` przez Storage Runtime ABI |
| [`45-storage-memory-query-execution.md`](./45-storage-memory-query-execution.md) | M13.4: pierwsze wykonanie Query Plan i typowany kursor providera memory |
| [`46-storage-sqlite-query-conformance.md`](./46-storage-sqlite-query-conformance.md) | M13.5: wspólna semantyka i trwałe wykonanie Query Plan przez SQLite |
| [`47-storage-sqlite-query-pushdown.md`](./47-storage-sqlite-query-pushdown.md) | M13.6: parametryzowany SQL pushdown SQLite z semantycznym fallbackiem |
| [`48-storage-postgresql-query-execution.md`](./48-storage-postgresql-query-execution.md) | M13.7: zweryfikowane wykonanie Query Plan przez PostgreSQL |
| [`49-storage-postgresql-query-pushdown.md`](./49-storage-postgresql-query-pushdown.md) | M13.8: parametryzowany SQL pushdown PostgreSQL z semantycznym fallbackiem |
| [`50-storage-generated-query-client.md`](./50-storage-generated-query-client.md) | M13.9: typowany klient named query i Golden E2E przez Host oraz provider |
| [`51-storage-task-query-binding.md`](./51-storage-task-query-binding.md) | M13.10: typowane wywołanie named query bezpośrednio z tasku `@server` |
| [`52-imported-storage-query-binding.md`](./52-imported-storage-query-binding.md) | M13.11: named query z osobnego modułu storage w projekcie |
| [`53-storage-import-alias.md`](./53-storage-import-alias.md) | M13.12: lokalny alias API storage z zachowaniem kanonicznej tożsamości Runtime |
| [`54-storage-query-developer-experience.md`](./54-storage-query-developer-experience.md) | M13.13: completion, hover, definition, Outline i zamknięcie ścieżki odczytu |
| [`55-storage-mutation-plan-ir.md`](./55-storage-mutation-plan-ir.md) | M14.1: abstrakcyjny Mutation Plan IR i bezpieczny zasięg zapisu |
| [`56-storage-mutation-source-projection.md`](./56-storage-mutation-source-projection.md) | M14.2: składnia mutation, AST, Structural IR i diagnostyki źródłowe |
| [`57-storage-runtime-mutation-lowering.md`](./57-storage-runtime-mutation-lowering.md) | M14.3: neutralne wywołanie `mutation.plan` i jawny wynik zapisu |
| [`58-storage-memory-mutation-execution.md`](./58-storage-memory-mutation-execution.md) | M14.4: pierwsze wykonanie Mutation Plan przez Host i provider memory |
| [`59-storage-sqlite-mutation-conformance.md`](./59-storage-sqlite-mutation-conformance.md) | M14.5: trwałe wykonanie i zgodność Mutation Plan memory ↔ SQLite |
| [`60-storage-sqlite-mutation-pushdown.md`](./60-storage-sqlite-mutation-pushdown.md) | M14.6: bezpieczny, parametryzowany delta pushdown Mutation Plan w SQLite |
| [`61-storage-postgresql-mutation-conformance.md`](./61-storage-postgresql-mutation-conformance.md) | M14.7: zgodne wykonanie Mutation Plan przez memory, SQLite i PostgreSQL |
| [`62-storage-postgresql-mutation-pushdown.md`](./62-storage-postgresql-mutation-pushdown.md) | M14.8: wspólny dowód delty i bezpieczny mutation delta pushdown PostgreSQL |
| [`63-storage-generated-mutation-client.md`](./63-storage-generated-mutation-client.md) | M14.9: typowany klient named mutation i Golden E2E przez Host oraz provider |
| [`64-storage-task-mutation-binding.md`](./64-storage-task-mutation-binding.md) | M14.10: named mutation wywoływana bezpośrednio z tasku `@server` |
| [`65-sdk-repository-ownership.md`](./65-sdk-repository-ownership.md) | M15.1–M15.2: publiczne Storage SDK ma jedno źródło w `junexis-sdk` |
| [`66-sdk-provider-template.md`](./66-sdk-provider-template.md) | M15.3: działający i bezpieczny szablon zewnętrznego dostawcy |
| [`67-sdk-provider-conformance.md`](./67-sdk-provider-conformance.md) | M15.4: przenośny zestaw zgodności i negatywny dowód fałszywej zdolności |
| [`68-sdk-provider-manifest-validation.md`](./68-sdk-provider-manifest-validation.md) | M15.5: ścisłe schematy manifestu, scenariusz testowy i kontrola projektu |
| [`69-sdk-provider-compatibility.md`](./69-sdk-provider-compatibility.md) | M15.6: jawna macierz i rozstrzyganie zgodności przed wykonaniem dostawcy |
| [`70-sdk-storage-plans-conformance.md`](./70-sdk-storage-plans-conformance.md) | M15.7: stanowy profil zgodności Query Plan i Mutation Plan |
| [`71-sdk-real-provider-onboarding.md`](./71-sdk-real-provider-onboarding.md) | M15.8: manifest 0.2 i rzeczywiści dostawcy jako konsumenci SDK |
| [`72-sdk-real-storage-plans-conformance.md`](./72-sdk-real-storage-plans-conformance.md) | M15.9: jeden powtarzalny profil Query Plan i Mutation Plan dla rzeczywistych dostawców |
| [`73-sdk-release-candidate.md`](./73-sdk-release-candidate.md) | M15.10: lokalny zestaw wydania SDK, weryfikacja integralności i czysty konsument |
| [`74-sdk-reproducible-build.md`](./74-sdk-reproducible-build.md) | M15.11: bitowo odtwarzalny zestaw wydania i opis pochodzenia związany z rewizją |
| [`75-sdk-signed-release.md`](./75-sdk-signed-release.md) | M15.12: lokalny podpis kryptograficzny manifestu i pochodzenia artefaktów SDK |
| [`76-sdk-release-trust.md`](./76-sdk-release-trust.md) | M15.13: magazyn zaufanych kluczy, rotacja, unieważnianie i ochrona przed cofnięciem polityki |
| [`77-sdk-authenticated-trust-store.md`](./77-sdk-authenticated-trust-store.md) | M15.14: klucz główny podpisujący politykę i pełna ścieżka zaufania do wydania SDK |
| [`78-sdk-m15-closure.md`](./78-sdk-m15-closure.md) | M15.15: zbiorcza bramka i formalne zamknięcie granicy SDK |
| [`79-platform-release-contract.md`](./79-platform-release-contract.md) | M16.1: wspólny manifest dostępności i integralności całej platformy |
| [`80-platform-release-compatibility.md`](./80-platform-release-compatibility.md) | M16.2: wersjonowany katalog zgodności komponentów platformy |
| [`81-platform-lsp-distribution.md`](./81-platform-lsp-distribution.md) | M16.3: samodzielna i uruchamialna paczka LSP Windows x64 |
| [`82-platform-lsp-installation.md`](./82-platform-lsp-installation.md) | M16.4: transakcyjna instalacja LSP i wspólne wykrywanie przez klientów |
| [`83-platform-vscode-extension.md`](./83-platform-vscode-extension.md) | M16.5: wersjonowany VSIX korzystający ze wspólnego LSP |
| [`84-platform-runtime-distribution.md`](./84-platform-runtime-distribution.md) | M16.6: wersjonowany zestaw Runtime dla .NET, embedded C i embedded Rust |
| [`85-platform-sdk-distribution.md`](./85-platform-sdk-distribution.md) | M16.7: SDK platformy, narzędzie autora i czysty konsument kontraktu |
| [`86-platform-provider-distribution.md`](./86-platform-provider-distribution.md) | M16.8: osobne zestawy Memory, SQLite i PostgreSQL związane z wydaniem SDK |
| [`87-platform-provider-installation.md`](./87-platform-provider-installation.md) | M16.9: transakcyjna instalacja i rozwiązywanie dostawców według wersji SDK |
| [`88-platform-project-provider-resolution.md`](./88-platform-project-provider-resolution.md) | M16.10: manifest projektu rozwiązuje zainstalowanego dostawcę i zapisuje bezpieczny plik blokady |
| [`89-platform-project-restore.md`](./89-platform-project-restore.md) | M16.11: powtarzalne i transakcyjne odtwarzanie dostawców projektu z lokalnego wydania |
| [`11-code-generation.md`](./11-code-generation.md) | M4: poziomy dowodu i kontrakty artefaktów backendów |
| [`12-cli-and-lsp.md`](./12-cli-and-lsp.md) | M5: kontrakt CLI, LSP i VS Code extension |
| [`13-compiler-runtime-baseline.md`](./13-compiler-runtime-baseline.md) | M5.5: bramka zgodności compiler/runtime przed M6 |
| [`15-narrative-runtime-trace.md`](./15-narrative-runtime-trace.md) | M6.1: polityka i protokół runtime trace |

Gramatyka formalna: [`../grammar/JunexisLang.g4`](../grammar/JunexisLang.g4)
