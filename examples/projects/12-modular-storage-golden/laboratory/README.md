# Laboratorium Storage — dane, które mają pamięć

To laboratorium prowadzi przez jeden pełny system domu. Czujnik tworzy
obserwację, Controller zapisuje ją przez abstrakcję storage, a Presenter
pokazuje stan bez znajomości bazy danych.

```text
Producer → Controller → Storage Runtime → provider
                 │
                 └→ Presenter
```

Nie zaczynamy od sterownika ani od awarii. Najpierw zapisujemy informację,
odczytujemy ją i rozumiemy miejsce każdego elementu. Potem wymieniamy provider,
a dopiero na końcu przejmujemy kontrolę nad integralnością i paczką wdrożeniową.

## Jedna podróż, trzy zakresy kontroli

| Etap | Pytanie | Rezultat |
|---|---|---|
| [Poznaj](01-poznaj.md) | Dlaczego obserwacja istnieje po restarcie? | działający system z SQLite |
| [Eksperymentuj](02-eksperymentuj.md) | Co zmieni się po użyciu memory? | porównanie dwóch providerów bez zmiany `.jnx` |
| [Przejmij kontrolę](03-przejmij-kontrole.md) | Skąd wiadomo, jaki kod i provider uruchamiamy? | zweryfikowany lockfile i samodzielny System Bundle |

Każdy etap zachowuje rytm:

```text
poznaj stan → zapisz hipotezę → wykonaj jedną zmianę
→ uruchom → odczytaj dowód → wyjaśnij wynik
```

## Uczciwa granica obecnego wydania

Publiczne paczki instalacyjne nie są jeszcze dostępne. Dzisiejsza,
zweryfikowana ścieżka wymaga zbudowanych lokalnie paczek providera i Storage
Runtime Host. Polecenia użytkownika pozostają poleceniami `junexis`; ścieżki do
artefaktów źródłowych są tymczasowym sposobem ich dostarczenia.
