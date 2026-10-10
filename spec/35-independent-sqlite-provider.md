# M12.5 — niezależny provider SQLite

> **Status:** VERIFIED
>
> **Pakiet:** `sqlite@0.1.0`
>
> **Implementacja:** `junexis-storage-providers/Junexis.Storage.Provider.Sqlite`

## Rezultat

SQLite wykonuje teraz ten sam neutralny kontrakt key-value co memory, lecz
przez osobny pakiet providera i trwały plik bazy. Provider nie należy do
generatora Go ani targetu `@server`:

```text
backend targetu
    → junexis.storage.runtime/0.1
    → sqlite@0.1.0
    → Microsoft.Data.Sqlite 10.0.11
    → SQLite
```

Referencyjny pakiet M12.5 jest samodzielnym pakietem Windows x64. Zawiera
entrypoint, dependency manifest, przypięte managed dependencies oraz natywną
bibliotekę `e_sqlite3.dll`. Loader hosta rozwiązuje zależności w kontekście
pakietu i współdzieli z nim wyłącznie assembly abstrakcji ABI.

## Operacje

Provider implementuje `put`, `get`, `list` i `delete` o tym samym wejściu i
wyniku co `memory@0.1.0`. Payload jest kanonicznym JSON-em, a wartości są
przekazywane do parametryzowanych komend SQLite.

`list` używa rzeczywistego kursora sterownika. Każde `next` otrzymuje nowy
deadline i token anulowania. Aktywny reader ma jawne ownership; do jego
zakończenia lub anulowania pozostałe operacje bindingu zwracają `busy`.

M12.6 rozszerzyło ten sam pakiet o aplikacyjny native SQL: `sql.query`,
`sql.scalar` i `sql.command`. Parametry oraz jawny kontrakt typowanych kolumn
opisuje [`36-native-sql-and-postgresql.md`](36-native-sql-and-postgresql.md).

## Binding

Wymagany `path` jest absolutną ścieżką pliku. `:memory:` jest odrzucane, bo
ulotność należy do osobnego providera. Opcjonalne ustawienia obejmują
`migrationsPath`, `busyTimeoutMs`, `operationTimeoutMs` i `maxPayloadBytes`.

Provider uruchamia WAL, foreign keys, synchronous FULL i ograniczony busy
timeout. Jedno prywatne połączenie należy do jednego bindingu. Dzięki temu
ownership transakcji oraz kursora jest deterministyczne.

## Transakcje

Jedna aktywna transakcja wymaga jawnego `transactionId` przy każdej operacji.
Operacja bez ID nie jest po cichu przypisywana do transakcji. Commit daje
potwierdzony sukces albo zachowawcze `unknown-outcome`; rollback i close nie
zatwierdzają skutku.

## Migracje

Provider stosuje uporządkowane pliki `.sql`, oblicza SHA-256 surowej treści i
zapisuje wynik w `_junexis_migrations`. Identyczny plik jest weryfikowany i
pomijany. Zmieniona zastosowana migracja zatrzymuje open klasą `migration`.

Nowa migracja oraz jej metadane są wykonywane w jednej transakcji. Niepewny
commit nie jest redukowany do timeoutu ani pozornego rollbacku.

## Klasyfikacja

- brak rekordu → `not-found`,
- constraint SQLite → `constraint`,
- realne `SQLITE_BUSY` lub `SQLITE_LOCKED` → `busy`,
- problemy otwarcia, I/O, readonly lub uszkodzenia → `unavailable`,
- historia migracji → `migration`,
- niepewny commit → `unknown-outcome`.

Tekst wyjątku sterownika nie przekracza hosta ABI.

## Dowód

Test M12.5 wykonuje prawdziwe SQLite i potwierdza:

- neutralne CRUD oraz sekwencyjne listowanie,
- commit, rollback i persistence po reopen,
- migrację i odrzucenie zmienionej historii,
- realny konflikt dwóch połączeń sklasyfikowany jako `busy`,
- trwałość pomiędzy dwoma procesami hosta,
- załadowanie sterownika i natywnej biblioteki wyłącznie z izolowanego pakietu,
- zamknięty skład pakietu Windows x64.

## Migracja bootstrapu

Generowany provider Go M11 nadal chroni dotychczasowy Golden Modular Storage
E2E. Nie jest miejscem dalszego rozwoju SQLite. Zostanie usunięty po wspólnej
bramce M12.7, kiedy wygenerowany klient będzie uruchamiał binding przez host.
