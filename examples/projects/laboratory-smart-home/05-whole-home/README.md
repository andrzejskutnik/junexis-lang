# Laboratorium Junexis — Dom 5

Ten przyrost rozszerza działający Dom 4 o salon, kuchnię i sypialnię. Każde
pomieszczenie publikuje ten sam kontrakt `SensorReading`, a wszystkie pomiary
przechodzą przez jedną regułę, jeden kontroler i jednego wykonawcę.

## Uruchomienie ze źródeł

Z katalogu `junexis-lang`:

```powershell
junexis check examples/projects/laboratory-smart-home/05-whole-home
junexis run examples/projects/laboratory-smart-home/05-whole-home --duration 30
```

Otwórz dynamiczny adres wypisany po słowie `PANEL`. Strona pokaże osobne karty
dla trzech pomieszczeń oraz podsumowanie całego domu.

## Co sprawdzić

1. Wszystkie karty dochodzą ze stanu „brak pierwszego potwierdzenia” do
   konkretnego pomiaru.
2. Salon i kuchnia potrafią niezależnie włączyć wentylację.
3. Sypialnia pozostaje poniżej wspólnego progu `1000 ppm`.
4. Najwyższy pomiar i liczba aktywnych wentylacji wynikają z potwierdzonych
   stanów pomieszczeń.
5. Zmiana jednego pomieszczenia nie usuwa ostatniego stanu pozostałych.

## Uczciwa granica

Trzy czujniki są niezależnymi zadaniami, ale wykonują się lokalnie jako
symulacja `@server`. `RoomReadingMerge` odbiera je w przewidywalnej kolejności,
aby początkujący mógł prześledzić każdy przebieg. Panel WWW jest dostarczonym
adapterem `extern`; nie jest jeszcze samodzielną aplikacją `@report`.

Automatyczna kontrola:

```powershell
& examples/projects/laboratory-smart-home/05-whole-home/verification/verify-whole-home.ps1
```

