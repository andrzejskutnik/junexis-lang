# M14.6 — bezpieczny delta pushdown Mutation Plan w SQLite

> **Status:** VERIFIED SAFE MUTATION DELTA PUSHDOWN
>
> **Zakres:** parametryzowane INSERT/UPDATE/DELETE po wspólnej walidacji semantyki

## Cel

M14.5 zapisywał poprawny wynik przez usunięcie i ponowne wstawienie całej
encji. Było to bezpieczne, lecz koszt zmiany jednego rekordu rósł wraz z
rozmiarem kolekcji. M14.6 zachowuje wspólny kernel jako źródło prawdy, ale
publikuje wyłącznie dowiedzioną różnicę:

```text
Mutation Plan + ograniczony snapshot
                 │
                 ▼
       wspólny kernel semantyczny
                 │
          snapshot przed/po
                 │
                 ▼
   dowód kształtu delty dla rodzaju mutacji
       ┌─────────┼─────────┐
       ▼         ▼         ▼
     INSERT    UPDATE    DELETE     wszystkie wartości jako parametry
       └─────────┴─────────┘
                 │
                 ▼
       jedna transakcja SQLite
```

Nie jest to jeszcze bezpośrednia kompilacja predykatu Mutation Plan do SQL.
Provider nadal odczytuje ograniczony snapshot i wykonuje referencyjny kernel.
Optymalizacja usuwa koszt pełnego przepisywania encji i nie tworzy drugiej
implementacji znaczenia mutacji.

## Warunki dowodu

Lowerer porównuje stan przed i po wykonaniu kernela. Akceptuje deltę tylko,
gdy odpowiada ona zadeklarowanemu rodzajowi operacji:

- `insert`: dokładnie jeden nowy rekord, bez zmian i usunięć;
- `update`: wyłącznie zmienione payloady istniejących kluczy;
- `delete`: liczba usuniętych kluczy jest równa `affectedRows`;
- `not-applied`: stan przed i po jest identyczny.

Fizyczna delta jest ograniczona do 1024 poleceń. Jeżeli dowodu nie można
przeprowadzić, provider zachowuje bezpieczny fallback M14.5 — atomową podmianę
snapshotu. Ta decyzja jest lokalna dla providera i nie zmienia Runtime ABI.

## Granice bezpieczeństwa

Tekst SQL jest stały. `bucket`, klucz oraz JSON payload trafiają wyłącznie do
parametrów `$bucket`, `$key` i `$payload`. Wszystkie payloady są sprawdzane
przed pierwszym poleceniem względem `maxPayloadBytes`. Każde polecenie musi
zmienić dokładnie jeden oczekiwany wiersz; rozbieżność ze zweryfikowanym
snapshotem jest błędem `conflict`.

Własna transakcja providera nadal gwarantuje atomowość. Przy jawnym
`transactionId` delta uczestniczy w transakcji klienta i podlega jej
commitowi albo rollbackowi. Brak potwierdzenia commita pozostaje
`unknown-outcome`.

## Dowód

Test lowerera przeprowadza insert, update i delete przez prawdziwy wspólny
kernel, po czym sprawdza dokładne, parametryzowane polecenia delty. Osobny test
odrzuca wynik, którego kształtu nie da się dowieść. Istniejące procesowe E2E
SQLite wykonuje zintegrowaną ścieżkę i potwierdza trwałość przez cztery
restarty. Pełna bramka zachowuje conformance memory ↔ SQLite.

Od M14.8 sam dowód różnicy znajduje się we wspólnym assembly Mutation Plan i
jest używany również przez PostgreSQL. SQLite nadal zachowuje własne polecenia
SQL, parametry i limity fizyczne.

```powershell
./scripts/verify-m14-6.ps1
```

## Granica M14.6

M14.6 nie dodaje jeszcze:

- kompilacji predykatu mutacji bezpośrednio do SQL;
- optymalizacji Mutation Plan przez PostgreSQL (wykonanie bazowe dodaje M14.7);
- typowanego wywołania named mutation z tasku `@server`;
- idempotency key i automatycznego retry po `unknown-outcome`.

M14.7 dodaje zgodne wykonanie Mutation Plan przez PostgreSQL. Jego własna
optymalizacja SQL pozostaje następnym etapem.
