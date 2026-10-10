# M13.9 — generowany klient nazwanych zapytań storage

> **Status:** VERIFIED GENERATED E2E
>
> **Target:** `@server` / Go
>
> **Pion:** `.jnx → Query Plan → klient Go → Storage Runtime Host → provider`

## Rezultat

Deklaracja `storage` przestała być wyłącznie kontraktem dla kompilatora i
providerów. Gdy program ma target `@server`, generator tworzy typowane API Go
dla każdego nazwanego zapytania:

```text
storage HomeData { query RecentObservations(...) ... }
                         │
                         ▼
HomeData_RecentObservationsParams
HomeData_RecentObservationsRow
HomeData_RecentObservations(ctx, provider, params)
                         │
                         ▼
query.plan / junexis.storage.runtime/0.1
                         │
                         ▼
memory | SQLite | PostgreSQL | zgodny provider zewnętrzny
```

Generator nie wybiera bazy i nie zawiera SQL. Ten sam kod klienta działa z
providerem wskazanym dopiero przez binding uruchomieniowy.

## Artefakt

`junexis build ... --target server` emituje dodatkowo:

```text
server/junexis/storage/queries/generated.go
```

Plik zawiera:

- strukturę parametrów dla każdej operacji;
- strukturę wiersza zgodną z końcowym shape Query Plan;
- kanoniczny, wersjonowany model i plan;
- funkcję wiążącą wartości uruchomieniowe i konsumującą kursor Runtime;
- mapowanie błędów transportu, Hosta, providera i dekodowania do
  `junexis.storage.Error`.

Nazwy są kwalifikowane modelem, dlatego zapytania o tej samej nazwie w dwóch
deklaracjach nie kolidują. Przykład `HomeData.RecentObservations` tworzy
`HomeData_RecentObservations`.

## Typy na granicy Go

| Storage ABI | Parametr Go | Pole wyniku Go |
|---|---|---|
| `int64` | `int64` | `int64` |
| `float64` | `float64` | `float64` |
| `bool` | `bool` | `bool` |
| `text` | `string` | `string` |
| `bytes` | `[]byte` | `[]byte` |
| `decimal` | kanoniczny `string` | kanoniczny `string` |
| `timestamp` | `time.Time` | kanoniczny `string` |
| `uuid` | kanoniczny `string` | kanoniczny `string` |
| `json` | `json.RawMessage` | `json.RawMessage` |

Nullable parametr i pole otrzymują wskaźnik. Wyniki `decimal`, `timestamp` i
`uuid` zachowują bezstratną postać wire; aplikacja może jawnie przekształcić je
do wybranego typu domenowego.

## Przepływ wykonania

Publiczna metoda `runtime.Provider.QueryPlan`:

1. wysyła wygenerowane wejście jako `query.plan/read`;
2. wymaga wyniku sekwencyjnego i identyfikatora kursora;
3. pobiera wiersze przez `next` aż do `done`;
4. anuluje kursor po błędzie;
5. zwraca surowe wiersze do typowanego dekodera operacji.

Klient zachowuje sekwencyjną ochronę istniejącego procesu Runtime. Komunikaty
błędów Hosta są przenoszone jako secret-safe `Cause`, dzięki czemu developer
widzi przyczynę, a nie samą klasę `internal`.

## Dowód Golden

Powtarzalna bramka:

```powershell
./scripts/verify-m13-9.ps1
```

buduje kompilator, referencyjny Host i provider memory, kompiluje
[`23-storage-generated-query-client.jnx`](../examples/grammar/23-storage-generated-query-client.jnx),
zapisuje obserwację przez wygenerowany neutralny klient Runtime, wykonuje
typowaną funkcję `HomeData_RecentObservations` i sprawdza odczyt jednego wiersza.
Test uruchamia Host oraz provider w osobnym procesie. Nie zastępuje ich atrapą.

## Granica M13.9

M13.9 domyka generowany klient zapytań dla bootstrapowego backendu Go. Na
granicy tego etapu nie dodawał:

- składni wywołania named query bezpośrednio w ciele tasku `.jnx`;
- klientów dla `@embedded` i `@report`;
- mutacji, relacji, joinów, agregacji i migracji modelu;
- produkcyjnego launchera rozwiązującego podpisany provider lock;
- natywnego typu domenowego Go dla UUID, decimal i timestamp wyniku.

Te granice nie zmieniają kontraktu providerów. Pierwsza z nich — połączenie
wyrażenia tasku z wygenerowaną funkcją — została zrealizowana w
[`51-storage-task-query-binding.md`](51-storage-task-query-binding.md), bez
dodawania SQL albo drivera do kompilatora.
