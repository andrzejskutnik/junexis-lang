# M12.9 — Laboratorium Junexis dla storage

> **Status:** VERIFIED

M12.9 przekłada wykonawczy pion M12 na doświadczenie edukacyjne. Jeden system
domu prowadzi użytkownika od pierwszego trwałego zapisu do świadomej kontroli
providera i System Bundle.

## Konstrukcja

```text
Poznaj
  zapis SQLite → restart → odtworzona obserwacja
        ↓
Eksperymentuj
  ten sam system → SQLite / memory → porównanie gwarancji
        ↓
Przejmij kontrolę
  lockfile → kontrolowana zmiana → odmowa startu → samodzielny bundle
```

Ścieżki znajdują się w
`examples/projects/12-modular-storage-golden/laboratory`. Nie są trzema
niezależnymi przykładami. Zachowują ten sam model domenowy, moduły, adapter i
granicę Presenter–Controller.

## Kontrakt edukacyjny

Każdy etap zawiera:

- cel systemu wyrażony językiem domeny;
- przewidywanie przed wykonaniem;
- dokładnie nazwaną zmianę;
- obserwowalny wynik;
- wyjaśnienie mechanizmu;
- punkt kontrolny i dowód do dalszej rozmowy.

Awaria aplikacji nie jest pierwszym doświadczeniem. Kontrolowana odmowa startu
pojawia się dopiero na etapie eksperckim i demonstruje działanie granicy
zaufania, gdy użytkownik zna już poprawny przepływ.

## Wykonywalne podstawy

- skrypt przygotowania rozpoznaje paczki `memory`, `sqlite` i `postgresql` oraz
  tworzy odpowiedni binding;
- wspólna próba Golden rozróżnia oczekiwaną trwałość SQLite i ulotność memory;
- launcher M12.8 ponownie weryfikuje pakiet względem lockfile;
- System Bundle zawiera host, provider i bezpieczny manifest deploymentu;
- PostgreSQL pozostaje ścieżką kontraktową do czasu wykonania live E2E.

## Granica dystrybucji

Laboratorium jest zweryfikowane ze źródłowymi artefaktami platformy. Do czasu
publicznych paczek instalacyjnych uczestnik musi wskazać lokalnie zbudowany host
i pakiet providera. Dokumentacja oznacza tę granicę wprost; nie przedstawia jej
jako gotowego doświadczenia bez checkoutu.
