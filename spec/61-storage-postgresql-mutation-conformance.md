# M14.7 — zgodne wykonanie Mutation Plan przez PostgreSQL

> **Status:** VERIFIED THREE-PROVIDER MUTATION EXECUTION
>
> **Zakres:** wspólna semantyka memory ↔ SQLite ↔ PostgreSQL i live PostgreSQL 17.5

## Cel

M14.7 rozszerza przenośny zapis na pierwszy zewnętrzny serwer bazodanowy.
Program oraz Runtime invocation nie wskazują Npgsql ani SQL:

```text
                mutation.plan / write
                         │
              Storage Runtime Host
                         │
             capability mutation-plan
          ┌──────────────┼──────────────┐
          ▼              ▼              ▼
       memory          SQLite       PostgreSQL
          └──────────────┼──────────────┘
                         ▼
       wspólny kernel + ten sam mutation-result
```

Provider PostgreSQL pakuje `Junexis.Storage.MutationPlan.dll`, deklaruje
capability `mutation-plan` i wykonuje tę samą kopertę
`junexis.storage.mutation-invocation/0.1` co pozostałe providery.

## Wykonanie bazowe

Dla każdego wywołania provider:

1. wymaga operacji `mutation.plan` i efektu `write`;
2. odczytuje nazwę encji ze zweryfikowanej koperty;
3. otwiera własną transakcję `RepeatableRead` albo dołącza do jawnego
   `transactionId` klienta;
4. materializuje uporządkowany snapshot ograniczony wspólnymi budżetami;
5. wykonuje referencyjny kernel Mutation Plan;
6. sprawdza wynik względem budżetów rekordów i payloadów;
7. publikuje wynik jako atomową podmianę encji przez parametryzowany JSONB;
8. zwraca `applied/not-applied` i `affectedRows`.

Bucket, klucze i payloady są parametrami Npgsql. Brak potwierdzenia własnego
commita zwraca `unknown-outcome`. Konflikt logicznego klucza jest rozpoznawany
przed publikacją, a błąd transakcyjny PostgreSQL zachowuje istniejące
przenośne klasy `busy`, `constraint`, `timeout` i `unavailable`.

Ta wersja celowo nie ma mutation pushdown. Najpierw ustala zgodność znaczenia;
bezpieczna optymalizacja fizycznego zapisu jest osobnym etapem M14.8.

## Wspólna bramka

Ten sam test bez znajomości implementacji providera wykonuje:

- insert i odczyt skutku przez Query Plan;
- konflikt powtórzonego klucza;
- update;
- update w jawnej transakcji i rollback;
- delete oraz ponowny `not-applied`.

PostgreSQL otrzymuje unikalną nazwę encji, a scenariusz sprząta wszystkie
rekordy. Test uruchamia się tylko przy ustawionym sekrecie
`JUNEXIS_TEST_POSTGRESQL`; brak serwera pozostaje jawnym `skipped`.

## Dowód live z 24 sierpnia 2026

Pełna solution providerów została wykonana na izolowanej bazie i roli w
kontenerze PostgreSQL 17.5. Wynik: **50/50 testów, 0 pominiętych**:

- PostgreSQL: 17/17;
- wspólna conformance: 9/9, w tym trzy providery Mutation Plan;
- SQLite: 14/14;
- memory: 10/10.

Po teście wymuszono usunięcie tymczasowej bazy i roli. Kontrola `pg_database`
oraz `pg_roles` zwróciła `0` zasobów z prefiksem `junexis_m147_`.
Istniejące ostrzeżenie niezgodności wersji collation administracyjnej bazy
`postgres` (2.36 wobec 2.41) nie dotyczyło izolowanej bazy utworzonej z
`template0` i kolacją `C`; nie było modyfikowane.

## Bramka odbioru

```powershell
$env:JUNEXIS_TEST_POSTGRESQL = '<connection-string>'
./scripts/verify-m14-7.ps1
```

Bez sekretu bramka nadal sprawdza kompilację, zamknięcie paczki, capability i
wszystkie testy niewymagające serwera, ale jawnie pomija trzy testy live
providera oraz trzy wspólne testy PostgreSQL.

## Granica M14.7

M14.7 nie dodaje jeszcze:

- delta pushdown ani bezpośredniego SQL pushdown mutacji PostgreSQL;
- typowanego wywołania named mutation z tasku `@server`;
- aplikacyjnego bloku wielu operacji;
- idempotency key i automatycznego retry po `unknown-outcome`.

M14.8 dodaje bezpieczny, parametryzowany delta pushdown PostgreSQL i
wyodrębnia jeden dowód różnicy współdzielony z SQLite.
