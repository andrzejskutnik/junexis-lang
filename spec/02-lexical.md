# Junexis Lang — Elementy leksykalne

> **Status:** EXPERIMENTAL  
> **Wersja:** 0.2.0  
> **Data:** 2026-06-23

Dokument opisuje wszystkie tokeny leksykalne języka Junexis. Podział na kategorie odpowiada kolejności reguł w gramatyce (`JunexisLang.g4`).

---

## 1. Słowa kluczowe — Deklaracje

Słowa kluczowe deklaracji top-level. Większość nie może być użyta jako
identyfikator użytkownika. Słowa M13 wymienione pod tabelą są kontekstowe,
aby nie unieważniać wcześniejszych programów.

| Keyword | Token | Status | Opis |
|---------|-------|--------|------|
| `task` | TASK | CORE | Deklaracja zadania/usługi |
| `struct` | STRUCT | CORE | Deklaracja struktury danych (product type) |
| `enum` | ENUM | CORE | Deklaracja enumeracji (sum type) |
| `fn` | FN | CORE | Deklaracja czystej funkcji |
| `pipeline` | PIPELINE | CORE | Deklaracja potoku danych |
| `use` | USE | CORE | Połączenie tasków w ciele `pipeline { }` |
| `device` | DEVICE | CORE | Deklaracja urządzenia sprzętowego |
| `import` | IMPORT | CORE | Import modułu |
| `narrative` | NARRATIVE | EXPERIMENTAL | Blok metadanych narracyjnych (opaque) |
| `deployment` | DEPLOYMENT | EXPERIMENTAL | Blok konfiguracji wdrożenia (opaque) |
| `supervisor` | SUPERVISOR | EXPERIMENTAL | Blok drzewa superwizji (opaque) |
| `circuit_breaker` | CIRCUIT_BREAKER | EXPERIMENTAL | Blok circuit breaker (opaque) |
| `quirks` | QUIRKS | EXPERIMENTAL | Blok obejść sprzętowych (opaque) |
| `extern` | EXTERN | EXPERIMENTAL | FFI bridge do kodu natywnego (D13); `extern task Name(...)` |
| `storage` | STORAGE | EXPERIMENTAL | Logiczny model i zapytania M13.2 |

Słowa `storage`, `entity`, `query`, `filter`, `project`, `sort`, `limit`,
`offset`, `key`, `asc`, `desc`, `mutation`, `insert`, `update`, `delete`,
`single`, `many`, `set` i `where` tworzą składnię deklaracji M13.2/M14.2. Gramatyka
traktuje je kontekstowo w pozycjach nazw i wyrażeń: istniejące `extern
storage`, parametr `limit` czy argument `storage=...` pozostają poprawne.

| Keyword | Token | Rola w `storage` |
|---------|-------|-------------------|
| `entity` | ENTITY | encja logicznego modelu |
| `query` | QUERY | nazwana operacja odczytu |
| `filter` | FILTER | warunek wyboru rekordów |
| `project` | PROJECT | jawny kształt wyniku |
| `sort` | SORT | kolejność wyniku |
| `limit` | LIMIT | maksymalna liczba rekordów |
| `offset` | OFFSET | opcjonalne przesunięcie limitu |
| `key` | KEY | klucz logicznej encji |
| `asc`, `desc` | ASC, DESC | kierunek sortowania |
| `mutation` | MUTATION | nazwana operacja zapisu |
| `insert`, `update`, `delete` | INSERT, UPDATE, DELETE | rodzaj zmiany danych |
| `single`, `many` | SINGLE, MANY | jawny zasięg mutacji |
| `set` | SET | przypisanie wartości pola |
| `where` | WHERE | warunek wyboru update/delete |

---

## 2. Słowa kluczowe — Protokoły

Protokół to osobna deklaracja top-level. Task nie wie o protokołach. Szczegóły w [`08-protocols.md`](./08-protocols.md).

| Keyword | Token | Status | Opis |
|---------|-------|--------|------|
| `protocol` | PROTOCOL | CORE | Deklaracja bindingu protokołu |

Nazwa protokołu po `protocol` to `IDENTIFIER` (`rest`, `grpc`, `ws`, `mqtt`, `coap`, `amqp`, lub własny).

```junexis
protocol rest {
    GET("/users",         ListUsers(query limit: u32))
    POST("/users",        CreateUser(body user: UserInput))
    GET("/users/{id}",    GetUser(path id: u32))
}

protocol grpc {
    ListUsers
    CreateUser
    GetUser
}

protocol myprotocol {
    // obsługa przez user-defined IR module
}
```

Ciało `task @protocol` jest **nieprzezroczyste** — parser nie analizuje jego treści. Specyfikacja protokołu trafia jako `TRIPLE_STRING` wewnątrz ciała tasku i jest interpretowana na etapie IR generation.

```junexis
task UserService() @server @rest {
    """
    GET  /users         -> ListUsers(query limit: u32)
    POST /users         -> CreateUser(body user: UserInput)
    GET  /users/{id}    -> GetUser(path id: u32)
    """
}
```

---

## 3. Słowa kluczowe — Typy wbudowane

| Keyword | Token | Status | Opis |
|---------|-------|--------|------|
| `Int` | INT_TYPE | CORE | Integer bez określonego rozmiaru |
| `Float` | FLOAT_TYPE | CORE | Float bez określonego rozmiaru |
| `Bool` | BOOL_TYPE | CORE | Wartość logiczna |
| `Text` | TEXT_TYPE | CORE | Łańcuch znakowy |
| `Symbol` | SYMBOL_TYPE | CORE | Symbol (atom, interned string) |
| `Time` | TIME_TYPE | CORE | Znacznik czasu |
| `Duration` | DURATION_TYPE | CORE | Czas trwania |
| `Map` | MAP_TYPE | CORE | Mapa `Map<K,V>` (D5: PascalCase) |
| `chan` | CHAN | CORE | Kanał punkt-punkt `chan<T>` |
| `queue` | QUEUE | CORE | Kolejka z persistence `queue<T>` |
| `stream` | STREAM | CORE | Strumień broadcast `stream<T>` |

Typy sized (`u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`) są parsowane jako identyfikatory (IDENTIFIER) i rozpoznawane na etapie IR generation — nie są osobnymi tokenami leksykalnymi. Zob. [`03-types.md`](./03-types.md#typy-sized).

---

## 4. Słowa kluczowe — Komunikacja

| Keyword | Token | Status |
|---------|-------|--------|
| `send` | SEND | CORE |
| `to` | TO | CORE |
| `recv` | RECV | CORE |
| `respond` | RESPOND | CORE |
| `from` | FROM | CORE |
| `broadcast` | BROADCAST | CORE |
| `parallel` | PARALLEL | CORE |

---

## 5. Słowa kluczowe — Przepływ sterowania

| Keyword | Token | Status |
|---------|-------|--------|
| `if` | IF | CORE |
| `else` | ELSE | CORE |
| `for` | FOR | CORE |
| `in` | IN | CORE |
| `match` | MATCH | CORE |
| `every` | EVERY | CORE |
| `return` | RETURN | CORE |
| `delay` | DELAY | CORE |
| `as` | AS | CORE |
| `log` | LOG | CORE |

---

## 6. Słowa kluczowe — Zmienne i stan

| Keyword | Token | Semantyka | Status |
|---------|-------|-----------|--------|
| `let` | LET | Powiązanie niemutowalne | CORE |
| `const` | CONST | Stała czasu kompilacji | CORE |
| `var` | VAR | Powiązanie mutowalne | CORE |
| `state` | STATE | Stan persistentny tasku | CORE |

---

## 7. Słowa kluczowe — Lifecycle hooks

| Keyword | Token | Status |
|---------|-------|--------|
| `on` | ON | CORE |

Składnia: `on IDENTIFIER '{' statements '}'`

Nazwa zdarzenia to zwykły `IDENTIFIER` — gramatyka nie ogranicza możliwych nazw. Kompilator waliduje dozwolone identyfikatory na etapie IR (nie w parserze), co umożliwia rozszerzanie zestawu hooków bez zmian w lekserze. Nieznany identyfikator → błąd `E0013`.

**Czym jest hook:** Punkt wejścia generowany przez kompilator — blok kodu, który runtime wykonuje automatycznie gdy nastąpi dane zdarzenie. Developer nie może podnieść hooka ręcznie ani zdefiniować własnych nazw zdarzeń w 0.2.

**Zdarzenia wbudowane 0.2:**

| Zdarzenie | Kiedy | Dostępne dla |
|-----------|-------|--------------|
| `start` | Raz po inicjalizacji tasku, przed pierwszym `every` lub odebraniem wiadomości | Wszystkie tasks |
| `connect` | Gdy peer nawiązuje połączenie przychodzące | Tasks z wejściowym `chan<T>` lub endpointem protokołu |
| `reconnect` | Gdy zerwane połączenie zostaje przywrócone | j.w. |
| `disconnect` | Gdy połączenie zostaje zerwane (peer się rozłączył lub timeout) | j.w. |

Szczegółowy opis semantyki i przykłady — [`04-declarations.md` § Lifecycle hooks (D15)](./04-declarations.md).

Wewnętrznie kompilator reprezentuje hooki jako `__on__<nazwa>` (np. `__on__connect`). Użytkownik nie ma dostępu do tej przestrzeni nazw — zob. sekcja 12.

---

## 8. Słowa kluczowe — Operatory logiczne

| Keyword | Token | Status |
|---------|-------|--------|
| `and`, `&&` | AND | CORE |
| `or`, `\|\|` | OR | CORE |
| `not`, `!` | NOT | CORE |

`true` i `false` są literałami — zob. sekcja 9. Formy symboliczne są
pełnymi aliasami słów kluczowych. AST normalizuje je do `and`, `or`, `not`, a
formatter powinien drukować postać słowną.

---

## 9. Literały

| Literał | Token | Przykłady | Status |
|---------|-------|----------|--------|
| Integer | INTEGER_LITERAL | `42`, `1_000_000`, `0` | CORE |
| Float | FLOAT_LITERAL | `3.14`, `1.0e10`, `0.5` | CORE |
| String (double-quoted) | STRING_LITERAL | `"hello"`, `"world\n"` | CORE |
| String (single-quoted) | STRING_LITERAL | `'hello'`, `'world'` | CORE |
| Raw string | RAW_STRING | `@"C:\path\no\escape"` | CORE |
| Triple string (multiline) | TRIPLE_STRING | `"""line1\nline2"""` | CORE |
| Boolean | TRUE / FALSE | `true`, `false` | CORE |
| Duration | DURATION_LITERAL | `5s`, `100ms`, `2h`, `30m`, `1d`, `500us`, `10ns` | CORE |
| Memory | MEMORY_UNIT_LITERAL | `32KB`, `512MB`, `8GB`, `256TB` | CORE |

**Uwaga do duration:** jednostki od najkrótszej do najdłuższej: `ns` (nanosekundy), `us` (mikrosekundy), `ms` (milisekundy), `s` (sekundy), `m` (minuty), `h` (godziny), `d` (dni).

**Uwaga do raw string:** znak `"` wewnątrz `@"..."` jest escapeowany przez podwojenie: `@"He said ""Hello"""`.

**Uwaga do triple string:** treść między `"""` a `"""` jest literalna — bez escaping (z wyjątkiem `"""`). Whitespace/newlines zachowane.

### Niezaimplementowane

| Literał | Przykład | Dlaczego |
|---------|---------|---------|
| Hex | `0x2A` | Patrz [`01-overview.md`](./01-overview.md#poza-zakresem-02) |
| Octal | `0o52` | j.w. |
| Binary | `0b1010` | j.w. |
| Interpolated string | `f"x = {x}"` | j.w. |
| Null | `null` | j.w. |
| Char | `'a'` (single char) | j.w. |

---

## 10. Operatory

### Arytmetyczne

| Operator | Token | Przykład | Status |
|----------|-------|---------|--------|
| `+` | PLUS | `a + b` | CORE |
| `-` | MINUS | `a - b`, `-x` | CORE |
| `*` | MULTIPLY | `a * b` | CORE |
| `/` | DIVIDE | `a / b` | CORE |
| `%` | MODULO | `a % b` | CORE |

### Przypisanie

| Operator | Token | Status |
|----------|-------|--------|
| `=` | ASSIGN | CORE |
| `+=` | PLUS_ASSIGN | CORE |
| `-=` | MINUS_ASSIGN | CORE |
| `*=` | MULT_ASSIGN | CORE |
| `/=` | DIV_ASSIGN | CORE |
| `%=` | MOD_ASSIGN | CORE |

### Porównanie

| Operator | Token | Status |
|----------|-------|--------|
| `==` | EQ | CORE |
| `!=` | NE | CORE |
| `<` | LT | CORE |
| `>` | GT | CORE |
| `<=` | LE | CORE |
| `>=` | GE | CORE |

### Specjalne

| Operator | Token | Użycie | Status |
|----------|-------|--------|--------|
| `->` | ARROW | Typ zwracany, endpoint, `rpc` | CORE |
| `::` | DOUBLE_COLON | Dostęp do wariantu enum (`Color::Red`), ścieżka modułu | CORE |
| `.` | DOT | Dostęp do pola (`point.x`) | CORE |
| `..` | DOT_DOT | Zakres (`1..10`) | CORE |
| `@` | AT | Adnotacja platformy/protokołu | CORE |
| `?` | QUESTION | Wyrażenie warunkowe ternarne (`cond ? a : b`) | CORE |
| `\|` | PIPE | Typ union (`T \| U`) | CORE |
| `&` | AMPERSAND | Referencja (`&x`) | CORE |
| `#` | HASH | Blok konfiguracji protokołu (`#openapi`, `#proto`) | CORE |
| `_` | UNDERSCORE | Wildcard w `match` | CORE |

### Niezaimplementowane

`**`, `<=>`, `<-`, `=>`, `?.`, `?:`, `|>`, `^`, `~`, `<<`, `>>`, `&=`, `|=`, `^=`, `<<=`, `>>=`

Patrz [`01-overview.md`](./01-overview.md#poza-zakresem-02).

---

## 11. Delimitery i interpunkcja

| Symbol | Token | Status |
|--------|-------|--------|
| `(` `)` | LPAREN, RPAREN | CORE |
| `{` `}` | LBRACE, RBRACE | CORE |
| `[` `]` | LBRACKET, RBRACKET | CORE |
| `:` | COLON | CORE |
| `;` | SEMICOLON | CORE |
| `,` | COMMA | CORE |

---

## 12. Identyfikatory

```
IDENTIFIER : [a-zA-Z_][a-zA-Z0-9_]*
```

Identyfikatory są case-sensitive. Nazwy single-uppercase (jak `Int`, `Float`, `Bool`, `Text`, `Symbol`, `Time`, `Duration`, `Map`) są zarezerwowane jako typy wbudowane — nie mogą być użyte jako identyfikatory użytkownika.

Znaki podkreślenia jako prefix (`_name`) są dozwolone i konwencjonalnie oznaczają identyfikatory prywatne.

Prefix podwójnego podkreślenia (`__name`) jest **zarezerwowany dla kompilatora**. Użycie `__` na początku nazwy przez użytkownika jest błędem kompilacji (E0003). Kompilator używa tej przestrzeni wewnętrznie — np. `__on__connect` jako reprezentacja lifecycle hooka `on connect`.

---

## 13. Komentarze i whitespace

| Rodzaj | Składnia | Status |
|--------|---------|--------|
| Liniowy | `// komentarz` | CORE |
| Blokowy | `/* komentarz */` | CORE |
| Whitespace | spacje, tabulatory, newlines | ignorowane poza literałami |

Komentarze blokowe **nie** mogą być zagnieżdżone.
