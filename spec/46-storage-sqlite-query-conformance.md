# M13.5 — zgodność Query Plan między memory i SQLite

> **Status:** VERIFIED MULTI-PROVIDER EXECUTION
>
> **Zakres:** wspólna semantyka Query Plan 0.1, memory, trwały SQLite
>
> **Poza zakresem:** SQL pushdown, PostgreSQL, mutacje, relacje i agregacje

SQL pushdown był poza zakresem tego kamienia milowego; jego pierwszy
zweryfikowany podzbiór opisuje
[`47-storage-sqlite-query-pushdown.md`](47-storage-sqlite-query-pushdown.md).

## Dlaczego drugi provider jest kamieniem milowym

M13.4 dowiódł, że plan może zostać wykonany. Jeden provider nie dowodził
jednak jeszcze przenośności znaczenia. M13.5 wysyła dokładnie tę samą kopertę
do dwóch różnych magazynów:

```text
junexis.storage.query-invocation/0.1
                  │
          ┌───────┴────────┐
          ▼                ▼
  memory snapshot    SQLite snapshot
          │                │
          └───────┬────────┘
                  ▼
      Query Plan semantic kernel
     filter → project → sort → limit
                  │
                  ▼
        ten sam typowany kursor
```

Program, lowerer i Runtime Host nie wiedzą, który z tych providerów wykona
plan. Różnica pozostaje w bindingu deploymentu.

## Referencyjny kernel semantyki

Kod interpretujący Query Plan został przeniesiony z providera memory do
assembly `Junexis.Storage.QueryPlan`. Zależy ono wyłącznie od publicznych
Abstractions Storage Runtime i jest dołączane do paczek memory oraz SQLite.

Kernel odpowiada za:

- walidację wersji, modelu, parametrów, argumentów i końcowego shape;
- dziewięć przenośnych typów wartości;
- predykaty porównań, `and`, `or`, `not` i null;
- projekcję, stabilny sort i limit z offsetem;
- kontrolowane błędy `constraint`.

Provider nadal odpowiada za wybór danych, transakcję, budżety, lifecycle
kursora i mapowanie awarii sterownika. Zewnętrzny provider nie ma obowiązku
używać referencyjnego assembly; jego semantykę rozstrzyga wspólna brama
conformance.

## Fizyczne wykonanie SQLite

SQLite przechowuje payload w `_junexis_records`. Dla `query.plan`:

1. odczytuje `sourceEntity` ze sprawdzonej koperty;
2. wykonuje parametryzowany scan bucketu o tej nazwie;
3. zachowuje porządek technicznego klucza jako deterministyczny porządek
   wejściowy;
4. materializuje ograniczony snapshot JSON;
5. przekazuje snapshot do wspólnego kernela;
6. oddaje gotowe wiersze przez zarządzany kursor SQLite.

Jawne budżety `maxQueryScanRecords` i `maxQueryScanBytes` chronią proces przed
nieograniczoną materializacją. Domyślnie wynoszą odpowiednio 100 000 rekordów
i 64 MiB payloadów. Przekroczenie daje `constraint`.

Ten etap nie udaje SQL loweringu: `filter`, `project`, `sort` i `limit` nie są
jeszcze przepychane do zapytania SQLite. Najpierw stabilizujemy identyczne
znaczenie między providerami; optymalizacja fizyczna może nastąpić bez zmiany
kontraktu języka.

## Conformance

Ten sam test dla memory i SQLite zapisuje zdarzenia, a następnie sprawdza:

- filtr logiczny łączący parametr tekstowy i typowany literał `int64`;
- projekcję pola nullable;
- sort po dwóch kluczach i limit;
- widoczność rekordu zapisanego wewnątrz jawnej transakcji;
- zniknięcie tego rekordu po rollbacku;
- tę samą kolejność oraz wartości wynikowe w obu providerach.

Procesowe E2E SQLite zapisuje rekord w pierwszym procesie Host, zamyka go,
otwiera ten sam plik w drugim procesie i odczytuje rekord przez `query.plan`.
Dowodzi to jednocześnie trwałości, kompletności paczki z kernelem oraz
działania kursora za granicą procesu.

## Co pozostaje otwarte

- pushdown Query Plan do parametryzowanego SQL;
- wykonanie tej samej koperty przez PostgreSQL;
- statystyki i optymalizator kosztowy;
- mapowanie logicznego modelu na dedykowane tabele i indeksy;
- mutacje, relacje, joiny i agregacje;
- pełne E2E od źródła `.jnx` przez wygenerowanego klienta targetu.
