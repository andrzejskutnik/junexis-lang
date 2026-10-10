# M15.15 — zamknięcie Junexis SDK

M15 jest zamknięty dla publicznych kontraktów SDK i lokalnie weryfikowalnego
łańcucha wydawniczego. Status wynika ze zbiorczego wykonania bramek M15.1–M15.14,
a nie z samej kompletności dokumentacji.

## Domknięty zakres

| Obszar | Dowód |
|---|---|
| Własność ABI | `junexis-sdk` jest jedynym źródłem publicznego Storage Runtime ABI |
| Autor rozszerzenia | może utworzyć, zweryfikować, sprawdzić zgodność i spakować dostawcę |
| Rzeczywiści dostawcy | Memory, SQLite i PostgreSQL używają wspólnych kontraktów i profilu planów |
| Kandydat wydania | ma zamknięty manifest, odtwarzalne paczki i informację o pochodzeniu |
| Autentyczność | podpis wydania prowadzi przez politykę kluczy do oddzielnego klucza głównego |
| Próby negatywne | wykrywają zmianę, podmianę, wygaśnięcie, unieważnienie i cofnięcie polityki |

Końcowe polecenie:

```text
./scripts/verify-m15.ps1
```

wykonuje testy rozwiązania, wszystkie czternaście bramek oraz kontrolę
publicznych schematów i dokumentów. PostgreSQL zachowuje warunkową próbę na
rzeczywistej bazie: brak skonfigurowanej wartości poufnej jest jawnym
pominięciem.

## Granica statusu

Zamknięcie M15 nie oznacza publicznego wydania Junexis. Oficjalny klucz
główny, bezpieczne przechowywanie części prywatnej, publiczne źródło paczek,
trwałe adresy pobierania, instalator i aktualizacje należą do następnego
kamienia milowego dystrybucji.

M15 dostarcza kontrakty i lokalne dowody, na których ta dystrybucja może zostać
zbudowana bez zmiany granicy SDK.
