# Junexis Lang — System typów

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

---

## Dwie warstwy systemu typów (Decyzja D2)

Junexis posiada dwie warstwy typów, celowo:

1. **Typy high-level** (`Int`, `Float`, `Bool`, `Text`, `Symbol`, `Time`, `Duration`) — semantyczne kategorie. Używane w idiomatycznym kodzie Junexis; kompilator dobiera reprezentację na platformę docelową.

2. **Typy sized** (`u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`) — precyzyjne typy numeryczne. Używane gdy rozmiar ma znaczenie: protokoły, embedded, binary data.

Obie warstwy współistnieją. Nie ma konwersji wymuszanej — developer wybiera odpowiednią warstwę.

```junexis
struct SensorReading {
    temperature: Float       // high-level: kompilator dobierze double/@server, float/@embedded
    raw_adc: u16             // sized: dokładnie 16-bit unsigned — zawsze
    timestamp: Time
}
```

---

## Typy high-level

### Definicja

| Typ | Token | Opis | Przykłady wartości |
|-----|-------|------|--------------------|
| `Int` | INT_TYPE | Liczba całkowita (platforma decyduje o rozmiarze) | `42`, `0`, `-7`, `1_000_000` |
| `Float` | FLOAT_TYPE | Liczba zmiennoprzecinkowa | `3.14`, `-0.5`, `1.0e10` |
| `Bool` | BOOL_TYPE | Wartość logiczna | `true`, `false` |
| `Text` | TEXT_TYPE | Łańcuch znakowy UTF-8 | `"hello"`, `@"C:\path"`, `"""multi"""` |
| `Symbol` | SYMBOL_TYPE | Interned string (atom) — wydajne porównania | `:ok`, `:error`, identifiers |
| `Time` | TIME_TYPE | Znacznik czasu (punkt w czasie) | generowany przez runtime |
| `Duration` | DURATION_TYPE | Czas trwania | `5s`, `100ms`, `2h`, `30m`, `500us` |

**Uwaga:** Typ `Symbol` jest opcjonalnie zapisywany z prefiksem `:` lub jako identifier w kontekście enum/match. Kompilator normalizuje.

### Mapowanie na platformy

| Typ | @server (Go/C#) | @embedded (C++) | @report (TypeScript) |
|-----|-----------------|------------------|----------------------|
| `Int` | `int64` / `long` | `int32_t` | `number` |
| `Float` | `float64` / `double` | `float` | `number` |
| `Bool` | `bool` | `bool` | `boolean` |
| `Text` | `string` | `const char*` / `std::string` | `string` |
| `Symbol` | `string` | `const char*` | `string` |
| `Time` | `time.Time` / `DateTime` | `uint32_t` (unix ms) | `Date` |
| `Duration` | `time.Duration` / `TimeSpan` | `uint32_t` (ms) | `number` (ms) |

---

## Typy sized

Parsowane jako identyfikatory (token IDENTIFIER), rozpoznawane w fazie IR generation — nie są osobnymi tokenami leksykalnymi. Oznacza to, że gramatyka jest syntaktycznie otwarta: `let x: u8 = 42` jest parsowane tak samo jak `let x: MyStruct = ...`. Kompilator w IRGenerator rozróżnia.

### Typy całkowite

| Typ | Zakres | Opis |
|-----|--------|------|
| `u8` | 0 .. 255 | unsigned 8-bit |
| `u16` | 0 .. 65535 | unsigned 16-bit |
| `u32` | 0 .. 4 294 967 295 | unsigned 32-bit |
| `u64` | 0 .. 18 446 744 073 709 551 615 | unsigned 64-bit |
| `i8` | -128 .. 127 | signed 8-bit |
| `i16` | -32 768 .. 32 767 | signed 16-bit |
| `i32` | -2 147 483 648 .. 2 147 483 647 | signed 32-bit |
| `i64` | -9 223 372 036 854 775 808 .. ... | signed 64-bit |

### Typy zmiennoprzecinkowe

| Typ | Precyzja | Opis |
|-----|----------|------|
| `f32` | ~7 cyfr dziesiętnych | IEEE 754 single |
| `f64` | ~15 cyfr dziesiętnych | IEEE 754 double |

### Mapowanie na platformy

| Typ | @server (Go) | @server (C#) | @embedded (C++) | @report (TS) |
|-----|-------------|--------------|-----------------|--------------|
| `u8` | `uint8` | `byte` | `uint8_t` | `number` |
| `u16` | `uint16` | `ushort` | `uint16_t` | `number` |
| `u32` | `uint32` | `uint` | `uint32_t` | `number` |
| `u64` | `uint64` | `ulong` | `uint64_t` | `bigint` |
| `i8` | `int8` | `sbyte` | `int8_t` | `number` |
| `i16` | `int16` | `short` | `int16_t` | `number` |
| `i32` | `int32` | `int` | `int32_t` | `number` |
| `i64` | `int64` | `long` | `int64_t` | `bigint` |
| `f32` | `float32` | `float` | `float` | `number` |
| `f64` | `float64` | `double` | `double` | `number` |

---

## Typy komunikacyjne

Typy pierwszoklasowe — fundamentalna część języka, nie biblioteka. Szczegóły semantyczne w [`05-communication.md`](./05-communication.md).

| Typ | Token | Semantyka | Przykład |
|-----|-------|-----------|---------|
| `chan<T>` | CHAN | Synchroniczne, punkt-punkt, zero strat, blokuje producenta | `chan<SensorData>` |
| `queue<T>` | QUEUE | Persistent, at-least-once, offline-first, wymaga ACK | `queue<Event>` |
| `stream<T>` | STREAM | Latest-wins, broadcast 1→N, może dropować | `stream<Metric>` |

---

## Typy złożone

### Mapa `Map<K,V>`

Token `MAP_TYPE` ('Map' — PascalCase, Decyzja D5). Generyczna mapa klucz-wartość.

```junexis
let config: Map<Text, Int> = { "retries": 3, "timeout": 30 }
```

- Klucz `K` musi być porównywalny (żaden typ nie jest explicite wykluczony w 0.2)
- Wartość `V` może być dowolnym typem

### Lista `[T]`

Homogeniczna lista elementów typu `T`.

```junexis
let sensors: [Text] = ["temp", "humidity", "pressure"]
let readings: [SensorData] = []
```

### Generyki użytkownika `Name<T, U, ...>`

Typy generyczne zdefiniowane przez użytkownika lub bibliotekę.

```junexis
struct Result<T> {
    value: T | None
    error: Text | None
}
```

W 0.2 generyki są parsowane składniowo, ale semantyczna weryfikacja (type bounds, variance) jest ograniczona — kompilator propaguje typy best-effort.

### Typ unii `T | U`

Suma typów. Wartość jest typu `T` albo `U`.

```junexis
fn parse(input: Text) -> Int | Error
state buffer: [u8] | None = None
```

Typ `None` to specjalny singleton union member — semantyczny odpowiednik "brak wartości". Nie jest tożsamy z `null` (patrz [`01-overview.md`](./01-overview.md#poza-zakresem-02) — `null` POZA zakresem 0.2).

---

## Typy zdefiniowane przez użytkownika

### `struct` — product type

```junexis
struct Point {
    x: Float
    y: Float
    label: Text = "origin"   // opcjonalne pole z wartością domyślną
}
```

Tworzenie instancji:

```junexis
let p = Point { x: 1.0, y: 2.0 }
let origin = Point { x: 0.0, y: 0.0, label: "O" }
```

### `enum` — sum type

```junexis
enum Status {
    Active,
    Inactive,
    Error(Text)         // wariant z danymi
}

enum Shape {
    Circle(Float),      // promień
    Rectangle(Float, Float),  // szerokosc, wysokosc
    Point
}
```

Dostęp do wariantu (pattern matching):

```junexis
match shape {
    Shape::Circle(r)      -> log("circle r={r}")
    Shape::Rectangle(w, h) -> log("rect {w}x{h}")
    Shape::Point          -> log("point")
    _                     -> log("unknown")
}
```

---

## Zasady kompatybilności typów

1. **Typy high-level nie są automatycznie kompatybilne z sized**: `Int` i `i32` to różne typy. Konwersja przez `as`:

   ```junexis
   let counted: Int = 42
   let raw: i32 = counted as i32
   ```

2. **Union types wymagają match**: Wartość `T | U` musi być wyodrębniona przez `match` lub destrukturyzację:

   ```junexis
   let result: Int | Error = parse("42")
   match result {
       x: Int  -> log("got {x}")
       e: Error -> log("error: {e}")
   }
   ```

3. **Brak automatycznego nullowania**: Nie ma niejawnego `null`. Pole opcjonalne to `FieldType | None`.

---

## Co NIE JEST typem w 0.2

| Element | Rationale |
|---------|-----------|
| `null` / `undefined` | Patrz [`01-overview.md`](./01-overview.md) |
| `Char` | `Text` obsługuje znaki; osobny `Char` nie ma uzasadnienia |
| Tablica o stałym rozmiarze `[T; N]` | Placeholder dla 0.3; embedded use-case specyficzny |
| Referencje `&T`, `&mut T` | Borrow semantics planowane v2.0; `&` jako referencja parsowana, ale semantyka niezaimplementowana |
| Tuple `(T, U)` | Zastąpione przez anonimowe `struct` lub `[T]` jednorodnych; rozważane 0.3 |
| `Option<T>` / `Result<T,E>` | Używaj `T | None` i `T | Error` (union types) |
