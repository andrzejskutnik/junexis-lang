# Provider pamięciowy zgodny ze storage 0.1

> **Status:** M12.7 VERIFIED
>
> **Pakiet:** `memory@0.1.0`

Memory uruchamia tę samą logikę domenową bez pliku bazy i usługi zewnętrznej.
Jest pełnym providerem kontraktu, nie atrapą wyników. Każdy nowy binding zaczyna
pusty, dlatego utrata danych po ponownym otwarciu jest jego jawną gwarancją.

```text
adapter domenowy → klient Storage Runtime → memory@0.1.0 → ulotny stan bindingu
```

Provider realizuje `put/get/list/delete`, deterministyczną listę, health,
commit i rollback. Transakcja używa izolowanej kopii stanu i wykrywa konflikt,
jeżeli stan bazowy zmienił się przed commit. Konfiguracja może ograniczyć
liczbę rekordów, rozmiar pojedynczego payloadu i łączny rozmiar danych.

Ten sam wspólny test M12.7 wykonuje wobec memory i providerów trwałych ten sam
scenariusz operacji. Różnica pojawia się dopiero w sprawdzeniu reopen: memory
ma być puste, SQLite i PostgreSQL mają zachować zatwierdzony rekord.

Szczegóły zawiera
[`34-independent-memory-provider.md`](34-independent-memory-provider.md), a
integrację klienta opisuje
[`37-generated-storage-runtime-client.md`](37-generated-storage-runtime-client.md).
