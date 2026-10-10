# M13.7 — wykonanie Query Plan przez PostgreSQL

> **Status:** VERIFIED THREE-PROVIDER EXECUTION
>
> **Zakres:** read-only Query Plan 0.1, PostgreSQL 14–18, wspólny kernel
>
> **Poza zakresem:** PostgreSQL SQL pushdown, mutacje, relacje i agregacje

## Cel

M13.7 dodaje trzecią fizyczną ścieżkę tej samej intencji programu:

```text
                 Query Plan 0.1
                       │
          ┌────────────┼────────────┐
          ▼            ▼            ▼
       memory        SQLite      PostgreSQL
          │            │            │
          └────────────┼────────────┘
                       ▼
             wspólna semantyka wyniku
```

PostgreSQL pozostaje niezależnym providerem. Kompilator i generatory targetów
nie importują Npgsql, nie znają connection stringa ani fizycznej tabeli.

## Fizyczne wykonanie

Provider deklaruje capability `query-plan` i pakuje assembly
`Junexis.Storage.QueryPlan`. Dla operacji `query.plan`:

1. wspólny kernel sprawdza kopertę i wskazuje `sourceEntity`;
2. provider wybiera rekordy `_junexis_records` parametrem `@bucket`;
3. techniczny klucz jest uporządkowany przez kolację `C`;
4. provider materializuje ograniczony snapshot JSON;
5. wspólny kernel wykonuje `filter`, `project`, `sort` i `limit`;
6. wynik wraca typowanym kursorem Storage Runtime ABI.

Wywołanie z `transactionId` czyta przez aktywną transakcję aplikacyjną.
Przekroczenie `maxQueryScanRecords` lub `maxQueryScanBytes` kończy się klasą
`constraint`.

## Dlaczego jeszcze bez pushdown

M13.6 dowiódł, że optymalizacja musi zachować dokładne znaczenie typów,
nullability i sortowania. M13.7 najpierw ustanawia przenośność trzeciego
backendu przez ten sam kernel. Lowering do operatorów JSONB i natywnego SQL
PostgreSQL będzie osobnym etapem z własnymi testami zgodności.

## Bramka conformance

Wspólny scenariusz dla każdego providera sprawdza:

- parametr tekstowy i literał `int64`;
- predykat logiczny;
- nullable oraz timestamp;
- projekcję, wielokluczowy sort i limit;
- widoczność zapisu w jawnej transakcji;
- rollback i powrót do wyniku bazowego.

Każde uruchomienie otrzymuje unikalną encję logiczną, dlatego współdzielony
serwer PostgreSQL nie miesza danych kolejnych prób. Rekordy są sprzątane po
udanym scenariuszu.

## Dowód live

23 sierpnia 2026 pełna bramka została wykonana na izolowanej bazie PostgreSQL
17.5 w kontenerze `postgres:17.5`. Połączenie trafiło przez publiczny port
testowego serwera, czyli nie omijało Npgsql ani granicy sieciowej providera.

Wynik odbioru:

- 37 testów zaliczonych, zero niepowodzeń i zero pominięć;
- zaliczone podstawowe PostgreSQL live E2E;
- zaliczone portable-store conformance;
- zaliczone Query Plan conformance z nullable, timestamp, sortem, limitem,
  widocznością transakcyjną i rollbackiem;
- pozostałe ścieżki memory i SQLite nadal zielone.

Test utworzył losową bazę i rolę `junexis_m137_*`, po czym usunął je w bloku
`finally`. Osobna kontrola katalogów systemowych PostgreSQL zwróciła zero
pozostałych zasobów. Sekret połączenia istniał tylko w pamięci procesu i nie
trafił do repozytorium, logu ani manifestu.

Serwer zgłosił ostrzeżenie o rozbieżności wersji collation administracyjnej
bazy `postgres` (2.36 wobec 2.41). Testowa baza powstała z `template0` i kolacją
`C`, więc ostrzeżenie nie zmieniło wyniku conformance. M13.7 nie wykonywało
automatycznej naprawy cudzej bazy administracyjnej.
