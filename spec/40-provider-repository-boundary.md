# M12.10 — niezależne repozytorium providerów

> **Status:** VERIFIED WORKSPACE

M12.10 rozdziela mechanizm wykonywania storage od implementacji konkretnych
baz danych także na poziomie własności kodu i cyklu wydawniczego.

```text
junexis-lang
  deklaracja + binding + lockfile + neutralny klient
                       │
                       ▼
junexis-runtime
  Abstractions + ABI + Host
                       │
                       ▼
junexis-storage-providers
  memory + sqlite + postgresql + wspólna bramka conformance
```

## Reguła własności

`junexis-runtime` jest właścicielem:

- `junexis.storage.runtime/0.1`;
- interfejsów fabryki, sesji, wartości i błędów;
- lifecycle bindingu i hosta procesu;
- ładowania jawnie wskazanego entrypointu;
- bezpieczeństwa transportu oraz sekretów.

`junexis-storage-providers` jest właścicielem:

- sterowników i zależności konkretnej bazy;
- mapowania typów oraz błędów silnika;
- migracji i native SQL wspieranego przez provider;
- manifestów `junexis.provider/0.1`;
- samodzielnych paczek i ich testów zgodności.

Generator targetu i implementacja `@server` nie są właścicielami żadnego
providera.

## Bramka M12.10

Nowe repo ma własne solution i polecenie `run-provider-gate.ps1`. Wykonuje
testy memory, SQLite, kontraktu PostgreSQL oraz jeden wspólny zestaw
conformance. W zweryfikowanym środowisku przeszły 23 testy; dwa testy live
PostgreSQL zostały pominięte z powodu braku `JUNEXIS_TEST_POSTGRESQL`.

Po M12.10 repo Runtime nadal budowało Abstractions, Host i ich testy. M15.2
przeniosło źródło Abstractions do `junexis-sdk`; Runtime zachowuje Host i testy,
lecz konsumuje kontrakt przez paczkę.

## Zależność developerska po M12.11

Przejściowe referencje między repozytoriami zostały usunięte. Kod produkcyjny
providerów pobiera wersjonowany pakiet `Junexis.Storage.Runtime.Abstractions`,
a testy procesowe dodatkowo `Junexis.Storage.Runtime.Host`. Repo providerów nie
kompiluje źródeł Runtime i nie wymaga ich układu w katalogu sąsiednim.

W workspace paczki są przekazywane przez lokalny feed. Publikacja w trwałym
rejestrze zmieni adres źródła, ale nie model zależności ani projekty providerów.
Szczegóły kontraktu SDK opisuje
[`41-storage-runtime-sdk.md`](41-storage-runtime-sdk.md).
