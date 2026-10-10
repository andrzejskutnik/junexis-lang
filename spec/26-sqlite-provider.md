# Referencyjny provider SQLite

> **Status:** M12.7 VERIFIED
>
> **Pakiet:** `sqlite@0.1.0`

SQLite jest trwałym, lokalnym providerem dla `junexis.storage.runtime/0.1`.
Jest osobnym pakietem .NET uruchamianym przez Storage Runtime Host. Generator
targetu nie zawiera jego kodu ani drivera.

```text
adapter domenowy → klient Storage Runtime → sqlite@0.1.0 → plik SQLite
```

Binding wymaga bezwzględnej ścieżki `path`. Opcjonalnie przyjmuje
`migrationsPath`, `busyTimeoutMs`, `operationTimeoutMs` i `maxPayloadBytes`.
Provider wykonuje neutralne `put/get/list/delete`, transakcje, health, migracje
SHA-256 oraz parametryzowane `sql.query`, `sql.scalar` i `sql.command`.

Własności:

- trwałość po zamknięciu i ponownym otwarciu bindingu,
- deterministyczna kolejność `list`,
- jedna jawna transakcja na binding,
- klasyfikacja blokady jako `busy`,
- niezmienna historia migracji,
- brak SQL i wartości parametrów w telemetrii.

Szczegóły implementacji i dowodu zawiera
[`35-independent-sqlite-provider.md`](35-independent-sqlite-provider.md), a
sposób użycia z wygenerowanym backendem opisuje
[`37-generated-storage-runtime-client.md`](37-generated-storage-runtime-client.md).
