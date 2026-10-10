# Laboratorium Junexis — Dom 7: pierwsza cisza

Ten przyrost kontrolowanie zatrzymuje pomiary salonu. Źródło nie publikuje
zastępczego stanu `offline`; kanał pomiarów naprawdę milczy. Osobny monitor
porównuje czas od ostatniego pomiaru z limitem `4500 ms`.

## Uruchomienie

Z katalogu `junexis-lang`:

```powershell
junexis check examples/projects/laboratory-smart-home/07-first-silence
junexis run examples/projects/laboratory-smart-home/07-first-silence --duration 60
```

Otwórz adres wypisany po słowie `PANEL`. Poczekaj na pomiar salonu `1250 ppm`
i włączoną wentylację. Następnie wybierz **Zatrzymaj źródło salonu**.

Panel pokaże kolejno:

1. źródło zostało zatrzymane, lecz limit ciszy jeszcze nie upłynął,
2. wiek ostatniego pomiaru przekroczył `4500 ms`,
3. monitor opublikował `silent`,
4. system przeszedł do `limited-source-silent`,
5. wykonawca zachował ostatni potwierdzony stan wentylacji salonu.

Po wybraniu **Wznów źródło** system nie wraca natychmiast do normalnego trybu.
Czeka na rzeczywisty świeży pomiar salonu.

## Automatyczna kontrola

```powershell
& examples/projects/laboratory-smart-home/07-first-silence/verification/verify-first-silence.ps1
```

Test potwierdza normalny przepływ, rzeczywistą ciszę, przekroczenie limitu,
zachowanie stanu wykonawcy oraz powrót dopiero po świeżym pomiarze.

