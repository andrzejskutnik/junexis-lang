# M12.7 — klient Storage Runtime i wspólna bramka providerów

> **Status:** VERIFIED
>
> **ABI:** `junexis.storage.runtime/0.1`

M12.7 usuwa ostatnie sprzężenie generatora `@server` z implementacją bazy.
Generator Go emituje kontrakt `storage.Store` i neutralnego klienta procesu,
ale nie emituje providerów memory, SQLite ani PostgreSQL i nie dodaje ich
driverów do `go.mod`.

```text
program .jnx
    → adapter domenowy storage.Store
    → wygenerowany klient Runtime
    → NDJSON: junexis.storage.runtime/0.1
    → niezależny host .NET
    → wskazany pakiet providera
    → memory | SQLite | PostgreSQL
```

## Jawny binding uruchomieniowy

| Zmienna | Znaczenie |
|---|---|
| `JUNEXIS_STORAGE_RUNTIME_HOST` | ścieżka hosta Storage Runtime |
| `JUNEXIS_STORAGE_PROVIDER_ENTRYPOINT` | entrypoint zweryfikowanego pakietu |
| `JUNEXIS_STORAGE_PROVIDER` | ID providera |
| `JUNEXIS_STORAGE_CONFIG_JSON` | obiekt konfiguracji bindingu |

To jest techniczna granica uruchomienia. Wartości sekretów nie należą do kodu
Junexis ani generowanych manifestów. Docelowy launcher musi dodatkowo porównać
entrypoint i hash z `junexis.providers.lock/0.1` przed uruchomieniem procesu.

## Zachowanie klienta

Klient wykonuje handshake i `open`, mapuje `put/get/list/delete`, health oraz
transakcje na ABI. Wynik `list` konsumuje przez `next`, a błędy transportu i
providera mapuje na wspólne klasy `storage.Error`. Dostęp do strumienia procesu
jest sekwencyjny i chroniony przed równoległym zapisem ramek.

## Jedna bramka zgodności

Repo `junexis-storage-providers` zawiera jeden zestaw prób przyjmujący wyłącznie
`IStorageProviderFactory`. Ten sam scenariusz sprawdza health, zapis, odczyt,
deterministyczną listę, commit, rollback i deklarowaną semantykę ponownego
otwarcia. Memory i SQLite są obowiązkowe. PostgreSQL wykonuje identyczny test,
gdy środowisko udostępnia `JUNEXIS_TEST_POSTGRESQL`.

## Dowód Golden

Golden Modular Storage E2E kompiluje ten sam serwer Go bez drivera SQLite.
Serwer uruchamia osobny host i pakiet `sqlite@0.1.0`, zapisuje obserwację,
wycofuje transakcję kontrolną, restartuje proces i odczytuje zatwierdzony
rekord. Wykonana próba zakończyła się `PASS`.

## Granica etapu

M12.7 nie publikuje jeszcze uniwersalnego launchera produkcyjnego ani paczki
System Bundle zawierającej host i wybrane providery. Nie zmienia też składni
deklaracji storage. Domyka natomiast wykonawczą niezależność generatora Go od
konkretnej technologii danych.
