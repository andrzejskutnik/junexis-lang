# M13.3 — lowering Query Plan do Storage Runtime

> **Status:** VERIFIED COMPILER CONTRACT
>
> **Kontrakt wejścia:** `junexis.storage.query-invocation/0.1`
>
> **Transport:** istniejący `junexis.storage.runtime/0.1`
>
> **Wykonanie przez provider:** poza M13.3

## Rezultat

M13.3 łączy abstrakcyjny plan M13.1/M13.2 z granicą procesu ustanowioną w
M12. Nie wprowadza drugiego runtime'u ani protokołu transportowego:

```text
storage/query w .jnx
        │
        ▼
model + Query Plan IR
        │ walidacja M13.1
        ▼
query.plan / read
junexis.storage.query-invocation/0.1
        │ istniejące invoke z M12
        ▼
junexis.storage.runtime/0.1
        │
        └── provider z capability query-plan (memory od M13.4)
```

Kompilator potrafi teraz zbudować pełny, deterministyczny deskryptor
wywołania. M13.3 nie twierdzi jeszcze, że host lub provider wykonuje operację.

## Deskryptor wywołania

Lowering tworzy:

| Pole | Wartość |
|---|---|
| `runtimeAbi` | `junexis.storage.runtime/0.1` |
| `action` | `invoke` |
| `operation` | `query.plan` |
| `effect` | `read` |
| `requiredCapabilities` | `query-plan` |
| `input.schemaVersion` | `junexis.storage.query-invocation/0.1` |

Deskryptor nie zawiera `requestId`, `bindingId`, timeoutu ani sekretów.
Uzupełnia je klient Runtime w chwili konkretnego wywołania. Dzięki temu
fingerprint opisuje zamiar i argumenty, a nie przypadkowe dane transportowe.

Kompletny dokument przykładowy znajduje się w
[`examples/contracts/storage-query-invocation-0.1.json`](../examples/contracts/storage-query-invocation-0.1.json).

## Co trafia do `input`

Wejście niesie cztery jawne części:

1. wersjonowany logiczny model danych;
2. nazwę, model i parametry planu;
3. encję źródłową oraz kroki w kolejności wykonania;
4. argumenty wywołania oraz wynikowy shape.

Plan zagnieżdżony w IR jest normalizowany do sekwencji:

```text
source Observation
  → filter
  → project
  → sort
  → limit
  → result { sensorName: text, value: float64, recordedAt: timestamp }
```

Provider nie musi odtwarzać kolejności ze struktury drzewa ani zgadywać
kształtu kursora. `result` jest wyliczony przez kompilator po wszystkich
projekcjach.

## Stabilne dyskryminatory

Wire contract nie serializuje nazw klas C# ani liczbowych wartości enumów.
Używa jawnych nazw:

- kroki: `filter`, `project`, `sort`, `limit`;
- wyrażenia: `field`, `parameter`, `literal`;
- predykaty: `comparison`, `logical`, `not`, `null`;
- porównania: `eq`, `ne`, `lt`, `le`, `gt`, `ge`;
- sortowanie: `asc`, `desc`;
- typy: `int64`, `float64`, `bool`, `text`, `bytes`, `decimal`, `timestamp`,
  `uuid`, `json`.

Nazwy te są kontraktem ABI 0.1 i nie zależą od języka implementacji klienta
lub providera.

## Wiązanie argumentów

Argumenty są dostępne dopiero podczas wywołania. Lowerer:

- wymaga dokładnie jednego argumentu dla każdego parametru;
- odrzuca argument nieznany, powtórzony i o innym typie;
- egzekwuje nullability;
- układa argumenty według deklaracji, niezależnie od kolejności wejścia;
- normalizuje wartości do kanonicznej postaci ABI.

Przykłady normalizacji:

| Wejście | Typ | Wire |
|---|---|---|
| `+0042` | `int64` | `42` |
| `1.500` | `float64` | `1.5` |
| `TRUE` | `bool` | `true` |
| UUID bez myślników | `uuid` | małe litery, format `D` |
| `{ "ok" : true }` | `json` | `{"ok":true}` |

`bytes` używa Base64, `decimal` formatu invariant, a `timestamp` ISO 8601 z
offsetem. Wartość null jest dozwolona wyłącznie dla parametru nullable.

## Walidacja i diagnostyka

Lowering zawsze uruchamia walidator M13.1. Błędy `SQPxxx` zatrzymują
utworzenie wywołania. Dopiero poprawny plan przechodzi walidację runtime:

| Kod | Znaczenie |
|---|---|
| `SQR010` | brak wymaganego argumentu |
| `SQR011` | argument przekazany wielokrotnie |
| `SQR012` | argument nieznany |
| `SQR013` | niezgodny typ argumentu |
| `SQR014` | null dla parametru non-nullable |
| `SQR015` | niepoprawna wartość kanoniczna |
| `SQR020` | niejawny literał null zamiast predykatu null |
| `SQR021` | niepoprawna wartość literału planu |

Nie ma ścieżki „best effort”. Provider nie otrzymuje planu, którego
kompilator nie potrafi jednoznacznie opisać.

## Determinizm

Serializer tworzy kanoniczny, zwarty JSON. Argumenty są uporządkowane według
deklaracji, kroki według znaczenia planu, a model według programu. SHA-256
tego JSON-u daje stabilny fingerprint. Różne tekstowe postaci tej samej
wartości, np. UUID wielkimi literami, prowadzą do tego samego dokumentu.

Fingerprint nie jest podpisem ani Build ID. Jest dowodem reprodukowalności
konkretnego deskryptora i może zostać później związany z Narrative.

## Granica odpowiedzialności

M13.3 nie dodaje:

- obsługi `query.plan` w referencyjnym hoście lub providerach;
- loweringu do SQL albo wykonania w pamięci;
- mapowania logicznych encji na fizyczny schemat;
- bindingu task → storage ani generowanego API tasku;
- mutacji, relacji, joinów i agregacji;
- polityki indeksów lub optymalizatora kosztowego.

Warunek następnego pionu — capability `query-plan`, wykonanie przez provider
memory i rzeczywisty kursor z tego samego deskryptora — został zrealizowany w
M13.4; zob.
[`45-storage-memory-query-execution.md`](45-storage-memory-query-execution.md).

## Dowód M13.3

Sześć testów potwierdza:

1. ścieżkę M13.2 source → model/plan → neutralne wywołanie;
2. kolejność kroków i końcowy shape;
3. deterministyczny JSON i fingerprint;
4. normalizację wszystkich dziewięciu typów ABI;
5. diagnostykę kompletności, typu, nullability i wartości argumentów;
6. bezwarunkowe zatrzymanie planu odrzuconego przez M13.1.
