# M14.5 — trwałe wykonanie Mutation Plan przez SQLite

> **Status:** VERIFIED PERSISTENT EXECUTION SLICE
>
> **Zakres:** zgodność memory ↔ SQLite, transakcja i trwałość po restarcie
>
> **Pushdown mutacji:** jeszcze nie; M14.5 używa wspólnego kernela

## Cel

M14.4 udowodnił znaczenie zapisu w pamięci procesu. M14.5 potwierdza, że ten
sam Mutation Plan zachowuje znaczenie w trwałym magazynie:

```text
                 junexis.storage.mutation-invocation/0.1
                                  │
                     Host [mutation-plan/write]
                                  │
                 Junexis.Storage.MutationPlan
                         ┌────────┴────────┐
                         ▼                 ▼
                       memory            SQLite
                    atomowy snapshot   transakcja + plik
                         └────────┬────────┘
                                  ▼
                       ten sam mutation-result
```

Język, Mutation Plan, Runtime ABI i klient nie zależą od wyboru providera.

## Wykonanie w SQLite

Provider SQLite deklaruje `mutation-plan`. Dla każdego wywołania:

1. sprawdza `mutation.plan/write` oraz wersję koperty;
2. odczytuje ograniczony snapshot wskazanej encji;
3. wykonuje wspólny kernel M14.4;
4. jeżeli nastąpiła zmiana, podmienia encję parametryzowanymi poleceniami SQL;
5. zatwierdza całość i zwraca `mutation-result`.

Bez `transactionId` wszystkie fazy działają w jednej własnej transakcji
`Serializable`. Z jawnym `transactionId` zmiana pozostaje niewidoczna poza
transakcją aż do commita i znika po rollbacku.

Provider nie interpoluje bucketu, klucza ani payloadu w SQL. Nadal obowiązują
limity `maxPayloadBytes`, `maxQueryScanRecords` i `maxQueryScanBytes`. Błąd
przed commitem nie publikuje częściowego snapshotu. Jeżeli po rozpoczęciu
commita SQLite nie pozwala potwierdzić wyniku, provider zwraca
`unknown-outcome`.

## Wspólna zgodność memory ↔ SQLite

Jedna bramka, bez wiedzy o implementacji providera, sprawdza:

- capability `mutation-plan`;
- insert i `affectedRows: 1`;
- konflikt ponownego insertu tego samego klucza;
- odczyt skutku przez istniejący `query.plan`;
- update i widoczność nowej wartości;
- update w transakcji oraz brak skutku po rollbacku;
- delete oraz późniejsze `not-applied`.

To ważniejszy dowód niż dwa osobne testy: pokazuje, że podmiana providera nie
zmienia znaczenia aplikacji.

## Dowód trwałości

Test paczki uruchamia Host z izolowanego katalogu i ładuje wyłącznie
`sqlite@0.1.0` wraz z zadeklarowanymi plikami. Kolejne procesy wykonują:

```text
proces 1: insert → shutdown
proces 2: query(7) → update(9) → shutdown
proces 3: query(9) → delete → shutdown
proces 4: query → pusty kursor
```

Wszystkie procesy używają jednego pliku SQLite. Paczka zawiera jawnie
`Junexis.Storage.MutationPlan.dll`, więc test nie korzysta z checkoutu źródeł.

## Granica M14.5

M14.5 nie dodaje jeszcze:

- natywnego pushdownu Mutation Plan do pojedynczego INSERT/UPDATE/DELETE;
- wykonania mutacji przez PostgreSQL;
- generowanego klienta i wywołania named mutation z tasku `@server`;
- aplikacyjnego bloku wielu operacji transakcyjnych;
- idempotency key, automatycznego retry i procedury recovery po
  `unknown-outcome`.

M14.6 domyka bezpieczny, parametryzowany delta pushdown SQLite. Bezpośrednia
kompilacja predykatu do SQL oraz wykonanie PostgreSQL pozostają kolejnymi
etapami.

## Bramka odbioru

```powershell
./scripts/verify-m14-5.ps1
```

Bramka odtwarza Runtime SDK i uruchamia pełny zestaw testów providerów, w tym
wspólną zgodność oraz izolowane procesowe E2E SQLite.
