# M19.7.1 — przenośny demonstrator i jawna symulacja

**Status:** `IMPLEMENTED`, natywnie zweryfikowano `win-x64` i `linux-x64`
(2026-09-11)

## Cel

Kanonicznym demonstratorem nie jest mały przykład składni, lecz działający
system „Dom, który zauważa i reaguje”. Ma trzy pomieszczenia, pomiary, reguły,
decyzje, wykonanie, panel, kontrolowane zaburzenia i odzyskiwanie działania.

M19.7.1 oddziela dwie prawdy:

- projekt źródłowy jest przenośny i buduje się dla całej macierzy M19;
- pełny dowód wykonania powstaje wyłącznie na systemie, na którym program
  rzeczywiście został uruchomiony.

Złożenie programu dla Linux albo macOS na Windows nie jest nazywane próbą
natywną.

Próba M19.7.1 ujawniła zależność `buildId` od położenia projektu. M19.7.3
usunęło fizyczną ścieżkę z materiału tożsamości, ujednoliciło zakończenia linii
i dodało jawną bramkę porównania tożsamości, topologii oraz zachowania.

## Jawna granica symulacji

`laboratory.json` deklaruje:

```json
{
  "execution": {
    "mode": "simulation",
    "deterministic": true,
    "hardwareFallback": false,
    "scenarioSeed": "smart-home-m19-7-1"
  }
}
```

Ta deklaracja nie jest tylko opisem. Publiczny stan działającej aplikacji
zwraca `execution_mode` oraz `hardware_connected`. Bramka wymaga wartości
`simulation` i `false`. Nieobecny czujnik fizyczny nie uruchamia ukrytego
zastępstwa — wybór symulacji jest częścią scenariusza.

## Dowód przenośności

Bramka kopiuje wyłącznie wersjonowane źródła projektu do nowego katalogu
tymczasowego. Następnie wykonuje `check`, `analyze` i `build`, a wygenerowany
serwer buduje jako:

| RID | Próba na Windows x64 | Próba na Linux x64 |
|---|---|---|
| `win-x64` | natywne uruchomienie symulacji | złożenie krzyżowe |
| `linux-x64` | złożenie krzyżowe | natywne uruchomienie symulacji |
| `osx-arm64` | złożenie krzyżowe | złożenie krzyżowe |

```powershell
./scripts/verify-m19-7-1.ps1
```

Windows x64 i Linux x64 zwróciły ten sam `buildId`:
`2793f4aa297ab2301e8e517da55f2ff571e11836d4d5d928ce7c4361419a1915`.
Na obu systemach demonstrator uruchomił trzy pomieszczenia, zachował jawny tryb
symulacji bez sprzętu i przeszedł pełny scenariusz wraz z restartem oraz
odtworzeniem trwałego stanu. Dowody znajdują się w:

- `evidence/m19-7-1/win-x64.json`;
- `evidence/m19-7-1/linux-x64.json`.

macOS ARM64 pozostaje bez natywnego uruchomienia.

M19.7.2 wykorzystuje ten projekt do tworzenia gotowych paczek Laboratorium dla
każdego systemu. M19.7.3 porównuje ich topologię, identyfikator budowania i
obserwowalne zachowanie.
