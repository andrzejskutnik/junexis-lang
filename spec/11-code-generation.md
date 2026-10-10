# Code generation and artifact acceptance (0.2 Experimental)

## Status

Ten dokument definiuje kontrakt eksperymentalnego Milestone 4. Backend C++, Go
lub TypeScript nie jest uznawany za działający tylko dlatego, że zwrócił tekst.

## Cztery rodziny artefaktów

| Część | Wejście | Artefakt bootstrapowy | Kierunek docelowy |
|---|---|---|---|
| 4a embedded | task + device | C++/PlatformIO | native AOT |
| 4b server | task | moduł Go | Junexis bytecode/VM, opcjonalnie AOT |
| 4c report | task | projekt TypeScript | WebAssembly |
| 4d integracja | pipeline + protocol + deployment | adaptery i bundle | wspólne ABI/wire format |

Backendy bootstrapowe nie definiują semantyki języka. Semantyka należy do IR i
wspólnych kontraktów runtime; języki hosta są tymczasowymi nośnikami wykonania.

## Poziomy dowodu

Każdy backend i przykład klasyfikuje się według najwyższego osiągniętego poziomu:

1. **L0 — lowering:** AST przechodzi do Structural IR i generator nie ulega awarii.
2. **L1 — artifact contract:** generator tworzy kompletny i bezpieczny zestaw
   plików wymagany dla platformy.
3. **L2 — native build:** artefakt przechodzi natywny toolchain (`pio run`,
   `go build`, `tsc --noEmit`).
4. **L3 — behavioral conformance:** uruchomiony artefakt zachowuje semantykę
   tasków, typów komunikacyjnych, stanu i błędów.
5. **L4 — distributed conformance:** co najmniej dwa backendy komunikują się
   przez wersjonowany wire format zgodnie z pipeline, protocol i deployment.

Niższy poziom nie może być opisywany jako dowód poziomu wyższego. Test tekstowego
fragmentu generatora jest testem regresji, a nie dowodem L2 lub L3.

## Minimalny kontrakt plików L1

### Wspólne artefakty narzędziowe

Każdy projektowy `compile/build` generuje również manifest debuggera:

- `debug/junexis.debug.toml`

To nie jest backend wykonawczy i nie uruchamia osobnego `artifact-gate`.
Manifest jest słownikiem między modelem Junexis a narzędziami Narrative:

- stabilizuje strategię `taskId` używaną przez TraceCollector,
- mapuje `taskId` na nazwę `task`, target i pozycję w źródle,
- mapuje `pipeline` i krawędzie przepływu,
- opisuje `device` oraz granice `extern`,
- przenosi `narrative` jako kontekst dla debuggera i mentora,
- deklaruje atrybuty korelacji: `payloadHash` oraz `narrative_id`.

Ten artefakt jest częścią kontraktu `compile`, ale nie dowodem L2. Jego rolą
jest umożliwienie debuggerowi i mentorowi odczytania zdarzeń runtime jako
elementów systemu Junexis, a nie tylko jako numerycznych `taskId`.

### 4a — embedded

- `embedded/junexis_app.cpp`
- `embedded/platformio.ini`
- `embedded/junexis_types.h`
- `embedded/junexis_config.h`
- `embedded/junexis/runtime.h`
- `<Task>.h` i `<Task>.cpp` dla każdego tasku `@embedded`

### 4b — server

- `server/main.go`
- `server/go.mod`
- `server/junexis/storage/contract.go` — gdy program używa storage;
- `server/junexis/storage/runtime/client.go` — neutralny klient procesu;
- `server/junexis/storage/queries/generated.go` — gdy źródło deklaruje named query.
- `server/junexis/storage/mutations/generated.go` — gdy źródło deklaruje named mutation.

### 4c — report

- `report/index.ts`
- `report/types.ts`
- `report/runtime.ts`
- `report/package.json`
- `report/tsconfig.json`

`schema.proto` jest artefaktem kontraktowym, gdy dokument zawiera `struct` lub
`enum`. Samo wygenerowanie schematu nie oznacza, że backendy używają Protobuf.

## Bramki wykonywalne

Kontrakt L1 jest uruchamiany przez `compile`, `validate-example` i `deploy`.
Naruszenie daje `G0001`; nieznany target — `G0002`.

Natywna bramka L2:

```text
junexis artifact-gate example.jnx
junexis artifact-gate example.jnx --target embedded
junexis artifact-gate example.jnx --target server
junexis artifact-gate example.jnx --target report
```

Polecenie wymaga odpowiednio PlatformIO, Go albo npm/TypeScript. Brak toolchainu
jest niepowodzeniem bramki, a nie pominiętym testem.

Stan zweryfikowany 2026-06-23 dla głównego korpusu:

| Backend | L1 | L2 |
|---|---:|---:|
| embedded | 10/10 | 10/10 (`pio run`) |
| server | 12/12 | 12/12 (`go build`) |
| report | 4/4 | 4/4 (`tsc --noEmit`) |

Jawna granica zewnętrzna pozwala zamknąć L2, ale bez bindingu celowo kończy się
błędem lub używa bootstrapowej implementacji C++. Go i TypeScript pozwalają
wstrzyknąć implementację do testu lub podczas startu aplikacji. Dopóki binding
nie zostanie dostarczony i wykonany, nie stanowi to dowodu L3.

Pierwszy powtarzalny dowód L3 obejmuje backend Go: wygenerowany task jest
kompilowany i wykonywany przez `go test` z wstrzykniętą implementacją `extern`.
Nie rozszerza to statusu L3 na cały korpus ani pozostałe backendy.

Pierwszy powtarzalny dowód L4 obejmuje granicę `@server → @report` dla
`stream<Text>`: wygenerowany serwer Go jest uruchamiany, wygenerowany klient
TypeScript kompilowany i wykonywany w Node, a payload przechodzi przez HTTP/JSON
w kopercie `junexis-wire/0.2`. Scenariusz zawiera pipeline, REST binding oraz
deployment. Nie rozszerza to L4 na MQTT, gRPC, WebSocket/MessagePack ani embedded.

## Baseline 4d

- `DistributedTopologyLowerer` tworzy protocol bindings, placements i krawędzie
  transportowe;
- `protocol rest` generuje typowane handlery Go i `openapi.json`;
- `deployment target { ... }` generuje placement/replicas w
  `deployment-plan.json` i `docker-compose.yml`;
- `wire-contract.json` zapisuje transport, encoding i wersję każdej krawędzi;
- błędne referencje tasków lub platform deploymentu zatrzymują kompilację.

## Granice aktualnego kontraktu

- wiele różnych urządzeń nie może docelowo współdzielić jednego obrazu firmware;
- niezwiązany `extern` zakończony błędem nie spełnia L3;
- Structural IR zachowuje surowe ciała `protocol` i `deployment`, ale wykonywalny
  podzbiór 0.2 jest loweringowany do Distributed Topology IR;
- REST/HTTP JSON jest wykonywalnym baseline; MQTT, gRPC, AMQP, CoAP oraz pełny
  WebSocket/MessagePack pozostają rozszerzeniami macierzy L4.
