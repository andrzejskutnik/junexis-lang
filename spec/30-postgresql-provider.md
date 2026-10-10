# Provider PostgreSQL dla `junexis.storage/0.1`

> **Status:** M12.7 CONTRACT VERIFIED / LIVE E2E PENDING
>
> **Pakiet:** `postgresql@0.1.0`

PostgreSQL jest niezależnym pakietem Storage Runtime opartym na Npgsql 10.0.3.
Nie należy do targetu `@server`, generatora Go ani konkretnego języka backendu.

```text
adapter dowolnego backendu
    → klient junexis.storage.runtime/0.1
    → postgresql@0.1.0
    → PostgreSQL
```

Binding wymaga `connectionString` dostarczonego przez deployment. Opcjonalnie
przyjmuje `migrationsPath`, `operationTimeoutMs` i `maxPoolSize`. Sekret nie
trafia do kodu, lockfile ani komunikatów błędów.

Provider realizuje neutralne `put/get/list/delete`, health, transakcje,
migracje i parametryzowane native SQL. SQLSTATE jest mapowany na przenośne
klasy, między innymi `constraint`, `busy` i `unavailable`.

Wspólny test conformance M12.7 jest identyczny jak dla memory i SQLite. Włącza
się po ustawieniu `JUNEXIS_TEST_POSTGRESQL`. Bez dostępnego serwera test
kontraktowy, ładowanie pakietu i klasyfikacja błędów przechodzą, natomiast
uczciwie nie deklarujemy wykonanego live E2E.

Szczegóły native SQL i stanu dowodu zawiera
[`36-native-sql-and-postgresql.md`](36-native-sql-and-postgresql.md).
