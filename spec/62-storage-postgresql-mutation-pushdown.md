# M14.8 — bezpieczny delta pushdown Mutation Plan w PostgreSQL

> **Status:** VERIFIED SAFE THREE-PROVIDER MUTATION DELTA
>
> **Zakres:** wspólny dowód delty, parametryzowane PostgreSQL INSERT/UPDATE/DELETE i live obserwacja SQL

## Cel

M14.7 ustanowił poprawne wykonanie PostgreSQL, ale publikował pełny snapshot
encji. M14.8 usuwa ten koszt bez tworzenia drugiej semantyki mutacji:

```text
Mutation Plan + snapshot przed
             │
             ▼
   wspólny kernel semantyczny
             │ snapshot po + affectedRows
             ▼
 PortableMutationDeltaPlanner
       ┌─────┼─────┐
       ▼     ▼     ▼
     added updated removed
       │     │     │
       ▼     ▼     ▼
     INSERT UPDATE DELETE     parametry Npgsql
       └─────┼─────┘
             ▼
      transakcja RepeatableRead
```

## Jeden dowód dla SQLite i PostgreSQL

Algorytm porównujący stan przed i po został przeniesiony do assembly
`Junexis.Storage.MutationPlan`. Nie jest częścią publicznego Runtime ABI;
udostępniono go wyłącznie zaprzyjaźnionym assembly providerów.

Wspólny planner dowodzi:

- `insert`: jeden dodany klucz, bez update i delete;
- `update`: brak zmian zbioru kluczy, tylko nowe payloady istniejących rekordów;
- `delete`: liczba usuniętych kluczy odpowiada `affectedRows`;
- `not-applied`: brak fizycznej różnicy;
- maksymalnie 1024 polecenia w jednej zoptymalizowanej delcie.

SQLite i PostgreSQL mapują ten sam wynik na własne stałe polecenia oraz składnię
parametrów. Brak dowodu albo przekroczenie limitu prowadzi do atomowego
fallbacku pełnego snapshotu właściwego dla providera.

## PostgreSQL

Lowerer emituje wyłącznie trzy stałe kształty SQL:

```sql
INSERT INTO _junexis_records(bucket, key, payload)
VALUES (@bucket, @key, @payload)

UPDATE _junexis_records SET payload = @payload
WHERE bucket = @bucket AND key = @key

DELETE FROM _junexis_records
WHERE bucket = @bucket AND key = @key
```

Bucket, klucz oraz JSONB są parametrami Npgsql. Cały wynik kernela jest
sprawdzany względem budżetów rekordów i payloadów przed pierwszym zapisem.
Każde polecenie musi zmienić dokładnie jeden oczekiwany wiersz; inny wynik
oznacza `conflict` ze zweryfikowanym snapshotem.

Nie jest to bezpośrednia kompilacja predykatu mutacji do pojedynczego SQL.
Provider nadal materializuje ograniczony snapshot i wykonuje wspólny kernel.
Optymalizacja dotyczy publikacji wyniku, dzięki czemu zgodność pozostaje
ważniejsza od specyfiki silnika.

## Dowód live

Test integracyjny instaluje tymczasowy trigger na `_junexis_records`, zapisuje
dwa rekordy i aktualizuje jeden przez `mutation.plan`. Dziennik triggera
potwierdza fizycznie:

```text
INSERT = 0
UPDATE = 1
DELETE = 0
```

Dowodzi to, że zintegrowana sesja PostgreSQL nie użyła fallbacku pełnej encji.
Test lowerera osobno sprawdza insert/update/delete, stały tekst SQL, parametry
oraz odrzucenie niespójnego wyniku. Regresja SQLite potwierdza, że wydzielenie
wspólnego plannera nie zmieniło wcześniejszej optymalizacji.

24 sierpnia 2026 końcowa bramka live na PostgreSQL 17.5 zaliczyła **67/67**
testów bez pominięć: Runtime 14/14 oraz solution providerów 53/53
(PostgreSQL 20, conformance 9, SQLite 14, memory 10). Tymczasowa baza i rola
`junexis_m148_*` zostały usunięte, a kontrola katalogów systemowych zwróciła
zero pozostałości. Ostrzeżenie collation administracyjnej bazy `postgres`
pozostało niezmienione.

## Bramka odbioru

```powershell
$env:JUNEXIS_TEST_POSTGRESQL = '<connection-string>'
./scripts/verify-m14-8.ps1
```

Bez connection stringa cztery testy live providera i trzy testy PostgreSQL we
wspólnej conformance są jawnie pomijane.

## Granica M14.8

M14.8 nie dodaje jeszcze:

- bezpośredniego predicate pushdown Mutation Plan do pojedynczego SQL;
- typowanego wywołania named mutation z tasku `@server`;
- bloku transakcyjnego wielu named operations;
- idempotency key, retry ani recovery po `unknown-outcome`.

Typowany klient named mutation dla bootstrapowego targetu Go został następnie
zrealizowany w [`63-storage-generated-mutation-client.md`](63-storage-generated-mutation-client.md).
