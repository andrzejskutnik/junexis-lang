# M12.6 — native SQL i niezależny provider PostgreSQL

> **Status:** CONTRACT VERIFIED / POSTGRESQL LIVE E2E PENDING
>
> **Implementacja providerów:** `junexis-storage-providers`
>
> **Pakiety:** `sqlite@0.1.0`, `postgresql@0.1.0`

## Rezultat

M12.6 dodaje do niezależnego Storage Runtime jawny SQL bez sprzężenia z
targetem ani językiem generatora:

```text
typowana operacja storage
    → adapter z parametryzowanym SQL-em
    → junexis.storage.runtime/0.1
    → sqlite@0.1.0 albo postgresql@0.1.0
    → sterownik providera
    → baza
```

SQL nie staje się składnią rdzenia Junexis. Jest kontrolowaną granicą adaptera
dla autora, który potrzebuje pełnej mocy bazy przed powstaniem abstrakcyjnego
modelu M13.

## Trzy operacje

| Operacja ABI | Wymagany efekt | Wynik |
|---|---|---|
| `sql.query` | `read` | przyrostowa sekwencja wierszy |
| `sql.scalar` | `read` | dokładnie jedna typowana wartość |
| `sql.command` | `write` lub `delete` | liczba zmienionych rekordów |

Provider musi jawnie deklarować capability `native-sql`. Samo posiadanie
capability `read` i `write` nie wystarcza.

## Parametry i wynik

```json
{
  "statement": "SELECT id, state FROM devices WHERE home_id = @homeId",
  "parameters": [
    { "name": "homeId", "type": "uuid", "value": "3d6f0a60-4dd4-47ec-94d8-086e17a39068" }
  ],
  "columns": [
    { "name": "id", "type": "uuid", "nullable": false },
    { "name": "state", "type": "json", "nullable": false }
  ]
}
```

Wartości są zawsze przekazywane do obiektów parametrów sterownika. Konkatenacja
danych użytkownika z SQL-em nie realizuje kontraktu. Dynamiczne identyfikatory
SQL nie mogą być parametrami i wymagają kontrolowanej allowlisty adaptera.

Przenośne typy ABI 0.1 to `int64`, `float64`, `bool`, `text`, `bytes`,
`decimal`, `timestamp`, `uuid` i `json`. Nie są nowymi keywordami ani
automatycznym rozszerzeniem typów języka. Adapter mapuje je do i z typów
deklarowanej operacji domenowej.

Provider sprawdza liczbę, nazwy, nullability oraz konwersję kolumn. Rozbieżność
daje `constraint`, zamiast obiektu o przypadkowym kształcie. `decimal` jest
przenoszony jako tekst invariant, `bytes` jako Base64, `timestamp` jako ISO
8601 z offsetem, a `json` pozostaje wartością JSON.

## PostgreSQL poza `@server`

Nowy pakiet `postgresql@0.1.0` działa przez host .NET i Npgsql 10.0.3. Ten sam
pakiet może obsłużyć klienta wygenerowanego do Go, C#, Javy albo Pythona.
Connection string jest sekretem bindingu, nie częścią `.jnx`, pakietu providera
ani wynikowej telemetrii.

Provider implementuje również neutralne `put/get/list/delete`, transakcje,
health oraz migracje SHA-256. Wspiera PostgreSQL 14–18 i mapuje między innymi:

- serialization failure, deadlock i lock unavailable → `busy`,
- connection failure i shutdown → `unavailable`,
- query cancellation → `timeout`,
- data, constraint i syntax errors → `constraint`,
- niepewny commit → `unknown-outcome`.

## Stan dowodu

`VERIFIED`:

- kontrakt typów, parametrów i jawnego shape,
- rzeczywiste query, scalar i command na SQLite,
- odporność parametru na próbę SQL injection,
- typowany kursor oraz odrzucenie niezgodnego wyniku,
- kompilacja pakietu PostgreSQL bez ostrzeżeń,
- zamknięta paczka, izolowane ładowanie przez host i manifest,
- walidacja bindingu i mapowanie SQLSTATE.

`PENDING`:

- live E2E na rzeczywistym PostgreSQL: open, migracja, zapis, rollback,
  restart procesu i odczyt.

Test live istnieje i uruchamia się po rozwiązaniu sekretu
`JUNEXIS_TEST_POSTGRESQL`. W środowisku wykonania M12.6 nie było Dockera,
`psql` ani skonfigurowanego endpointu. Brak tego zasobu nie jest przedstawiany
jako wykonany dowód.

## Następny etap

M12.7 zbuduje jeden zestaw conformance dla memory, SQLite i PostgreSQL oraz
przepnie Golden Storage E2E z bootstrapowych implementacji Go na klienta
niezależnego Storage Runtime.
