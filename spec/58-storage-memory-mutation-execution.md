# M14.4 — pierwsze wykonanie Mutation Plan przez provider memory

> **Status:** VERIFIED EXECUTION SLICE
>
> **Zakres:** Runtime Host, capability, wspólny kernel i provider memory
>
> **Trwałe providery:** SQLite i PostgreSQL jeszcze nie wykonują Mutation Plan

## Cel

M14.3 kończył się neutralnym deskryptorem. M14.4 przeprowadza go przez pierwszą
rzeczywistą granicę wykonania i dowodzi skutku istniejącym odczytem:

```text
mutation .jnx
    → Mutation Plan
    → mutation.plan / write
    → Storage Runtime Host
    → provider memory [mutation-plan]
    → insert / update / delete
    → mutation-result
    → query.plan potwierdza nowy stan
```

Nie jest to jeszcze wygodne wywołanie mutation z tasku. Jest to jednak pełne,
provider-neutralne wykonanie kontraktu zapisu od granicy Runtime do danych.

## Odpowiedzialność Host

Runtime SDK 0.1 udostępnia capability `mutation-plan` oraz stałe:

| Kontrakt | Wartość |
|---|---|
| operacja | `mutation.plan` |
| efekt | `write` |
| invocation | `junexis.storage.mutation-invocation/0.1` |
| plan | `junexis.storage.mutation-plan/0.1` |
| wynik | `junexis.storage.mutation-result/0.1` |

Host wymaga efektu `write`, ogólnej capability `write` i szczegółowej
`mutation-plan`. Provider bez tej deklaracji otrzymuje kontrolowane
`runtime.capability-unsupported`. Host nie interpretuje modelu, przypisań ani
predykatu i nie generuje SQL.

## Wspólny kernel Mutation Plan

Assembly `Junexis.Storage.MutationPlan` należy do repo providerów, nie do
generatora targetu ani konkretnej bazy. Kernel:

- ponownie waliduje wersje, model, encję, klucz, parametry i argumenty;
- interpretuje dziewięć typów Storage ABI;
- sprawdza przypisania, predykaty i jawny zasięg `single/many`;
- wykonuje `insert`, `update` i `delete` na stabilnym snapshotcie;
- zwraca atomowy stan zastępczy i jawny wynik;
- klasyfikuje konflikt klucza jako `conflict`, a wadliwy plan jako
  `constraint`.

Provider nie ufa temu, że kompilator wcześniej sprawdził źródło. Ta sama
koperta może dotrzeć przez proces, inną wersję narzędzia albo własnego klienta.

## Semantyka memory

Każda encja nadal odpowiada bucketowi. Insert tworzy techniczny, deterministyczny
klucz rekordu z logicznego klucza głównego, ale to mapowanie nie przecieka do
Mutation Plan. Przed publikacją provider kontroluje konflikt logicznego klucza
oraz wszystkie limity pamięci.

Update i delete najpierw wyznaczają komplet dopasowań. Jeżeli mutacja `single`
pasuje do więcej niż jednego rekordu, cały zapis jest odrzucany bez częściowego
skutku. Zero dopasowań daje potwierdzony wynik `not-applied` i
`affectedRows: 0`.

Zmiana bez `transactionId` jest atomową podmianą snapshotu bindingu. Z
`transactionId` zmienia wyłącznie snapshot transakcji, więc istniejące commit,
rollback i wykrywanie konfliktu wersji zachowują swoje znaczenie.

## Dowód pionowy

Test procesowy wykonuje przez Host następujący rytuał:

1. otwiera binding memory;
2. wykonuje insert przez `mutation.plan`;
3. odczytuje rekord istniejącym `query.plan`;
4. wykonuje update i ponownie odczytuje zmienioną wartość;
5. wykonuje delete i potwierdza pusty kursor query;
6. sprawdza konflikt powtórzonego klucza oraz `not-applied` po drugim delete;
7. odrzuca `single` pasujące do dwóch rekordów i potwierdza brak częściowego zapisu;
8. potwierdza brak skutku insertu po rollbacku transakcji;
9. sprawdza capability i kompletność niezależnej paczki providera.

## Granica M14.4

M14.4 nie dodaje jeszcze:

- wywołania named mutation bezpośrednio z tasku `@server`;
- generowanego klienta mutacji;
- wykonania przez SQLite lub PostgreSQL i SQL pushdown;
- upsertu, raw SQL w DSL ani transakcji wielooperacyjnej w języku;
- idempotency key, automatycznego retry i recovery `unknown-outcome`.

Naturalnym następnym krokiem M14.5 jest trwałe wykonanie przez SQLite na tym
samym kontrakcie i wspólna bramka zgodności memory ↔ SQLite.

M14.5 zrealizował ten krok; wynik i granicę trwałości opisuje
[`59-storage-sqlite-mutation-conformance.md`](59-storage-sqlite-mutation-conformance.md).

## Bramka odbioru

```powershell
./scripts/verify-m14-4.ps1
```

Bramka sprawdza Runtime Host, pakuje lokalne Runtime SDK i uruchamia pełną
macierz providerów. Dowód memory nie wymaga serwera bazy ani sekretów.
