# M12.4 — niezależny provider memory

> **Status:** VERIFIED
>
> **Pakiet:** `memory@0.1.0`
>
> **Implementacja:** `junexis-storage-providers/Junexis.Storage.Provider.Memory`

## Rezultat

M12.4 jest pierwszym wykonaniem `junexis.storage/0.1` przez provider, który nie
należy do generatora targetu. Jest ładowanym pakietem .NET dla niezależnego
Storage Runtime Host i komunikuje się wyłącznie przez
`junexis.storage.runtime/0.1`.

Target, backend i provider pozostają rozdzielone. `@server` nie jest wpisany do
manifestu pakietu, a provider nie importuje wygenerowanego programu Go.

## Kontrakt operacji

Provider implementuje neutralne operacje key-value:

- `put(bucket, key, payload)` z efektem `write`,
- `get(bucket, key)` z efektem `read`,
- `list(bucket)` z efektem `read` i sekwencyjnym wynikiem,
- `delete(bucket, key)` z efektem `delete`.

Payload jest kanonicznym JSON-em na granicy ABI. Typ domenowy istnieje przed
serializacją i po odczycie; provider memory nie staje się systemem typów
Junexis ani ORM-em.

## Semantyka

- nowy binding jest pusty,
- zamknięcie usuwa cały stan,
- wartości są kopiowane defensywnie,
- `list` jest uporządkowane rosnąco według klucza,
- `delete` jest idempotentne,
- brak rekordu daje `not-found`,
- błędna operacja, efekt, konfiguracja albo przekroczenie limitu daje
  kontrolowaną klasę błędu,
- provider deklaruje i wykonuje health,
- operacje respektują anulowanie przekazane przez host.

## Transakcja i konflikt

Jedna aktywna transakcja pracuje na snapshotcie. Commit publikuje kopię
atomowo, rollback ją odrzuca. Każda zmiana stanu bazowego zwiększa wersję.
Jeśli stan zmienił się pomiędzy begin i commit, commit zwraca `conflict`, a
snapshot nie jest publikowany.

Jest to zgodne znaczenie z bootstrapowym providerem M11, ale callback Go został
zastąpiony neutralnym rytuałem `begin → invoke(transactionId) → commit/rollback`.

## Ograniczona pamięć

Domyślne limity bindingu obejmują buckety, rekordy, rozmiar pojedynczego
payloadu i łączny rozmiar payloadów. Można je jawnie zmienić przez
`maxBuckets`, `maxRecords`, `maxPayloadBytes` i `maxTotalPayloadBytes`.

Brak konfiguracji oznacza bezpieczny profil domyślny, a nie pamięć bez limitu.
Provider nie przyjmuje sekretu ani connection stringa.

## Pakiet i dowód procesu

Build tworzy `provider-package` z manifestem `junexis.provider/0.1` i
entrypointem DLL. Test uruchamia osobny Storage Runtime Host, przekazuje mu
entrypoint pakietu i wykonuje zapis oraz odczyt przez NDJSON. Pozostałe testy
sprawdzają pełny lifecycle, sekwencję, transakcje, konflikt, ulotny reopen i
limity.

## Migracja bootstrapu

Generowany pakiet Go `server/junexis/storage/memory` nadal istnieje, aby nie
zerwać Golden Modular Storage E2E M11 przed wspólną bramką. Od M12.4 nie jest
jednak docelową implementacją ani miejscem dalszego rozwoju providera memory.

Usunięcie go z generatora nastąpi po M12.7, gdy memory, SQLite i PostgreSQL
przejdą jeden conformance gate i klient wygenerowanego backendu będzie
uruchamiał binding przez nowy host.
