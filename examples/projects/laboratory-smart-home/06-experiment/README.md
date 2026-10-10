# Laboratorium Junexis — Dom 6: Eksperymentuj

Ten przyrost zachowuje trzy pomieszczenia z Domu 5 i ujawnia trzy decyzje
konfiguracyjne: próg jakości powietrza, opóźnienie komunikacji oraz maksymalny
wiek pomiaru.

## Uruchomienie ze źródeł

Z katalogu `junexis-lang`:

```powershell
junexis check examples/projects/laboratory-smart-home/06-experiment
junexis run examples/projects/laboratory-smart-home/06-experiment --duration 90
```

Otwórz adres wypisany po słowie `PANEL`. Zacznij od profilu bazowego, a potem
uruchamiaj kolejno: **Próg 1400**, **Opóźnienie** i **Granica świeżości**.

Każda zmiana otrzymuje numer rewizji i zapisaną hipotezę. Formularz nie zmienia
systemu do chwili wybrania przycisku „Uruchom eksperyment”.

## Bezpieczne zachowanie

Pomiar poza budżetem świeżości nie jest używany do wydania nowego polecenia.
Wykonawca publikuje `held-stale-reading` i zachowuje ostatni potwierdzony stan
danego pokoju. Czujniki nadal pracują — nie jest to scenariusz awarii.

## Automatyczna kontrola

```powershell
& examples/projects/laboratory-smart-home/06-experiment/verification/verify-experiment.ps1
```

Test sprawdza profil bazowy, zmianę progu, kontrolowane opóźnienie, przekroczenie
budżetu świeżości oraz reset.

