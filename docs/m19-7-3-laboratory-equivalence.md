# M19.7.3 — równoważność Laboratorium

## Pytanie kontrolne

Trzy paczki nie mogą oznaczać trzech podobnych programów. Mają reprezentować
ten sam logiczny system. M19.7.3 sprawdza dlatego osobno:

1. tożsamość procesu budowania,
2. topologię i kontrakty komunikacji,
3. obserwowalne zachowanie demonstratora.

## Kanoniczny Build ID

Build ID preflightu zależy od treści źródła i uporządkowanej listy targetów.
Polecenia `analyze`, `build` i `package` korzystają z tej samej wartości. Nie
zawiera ona fizycznej ścieżki projektu. Przed obliczeniem skrótu zakończenia
linii CRLF i LF są sprowadzane do LF. Ten sam projekt przeniesiony do innego
katalogu lub pobrany na innym systemie zachowuje więc tożsamość.

Zmiana treści nadal zmienia Build ID. Bezwzględne ścieżki pozostają w lokalnych
rachunkach i mapach diagnostycznych, lecz nie uczestniczą w logicznej
tożsamości procesu budowania.

## Odcisk topologii

Bramka odczytuje obraz analizy z System Bundle każdej paczki. Tworzy kanoniczną
projekcję symboli, komunikacji, relacji semantycznych oraz metryk topologii.
Pomija fizyczne nazwy katalogów, zachowując pozycje w źródle. Wszystkie trzy
warianty muszą otrzymać ten sam SHA-256 projekcji.

## Odcisk zachowania

Na bieżącym systemie wykonywany jest pełny scenariusz domu w izolowanym
katalogu stanu. Odcisk obejmuje tryb symulacji, trzy pomieszczenia, decyzję po
awarii czujnika, zmianę progu, automatyczne i ręczne odzyskiwanie działania,
ograniczone przeciążenie oraz bezpieczny reset. Pomija numery zdarzeń i dane
zależne od czasu wykonania.

Interfejs WWW sprawdzany w tym scenariuszu należy do demonstratora
inteligentnego domu. Nie jest konstruktem języka ani osobnym komponentem
platformy Junexis.

## Dowód i granica

```powershell
./scripts/verify-m19-7-3.ps1 -BuildAll
```

Skrypt znajduje się w repozytorium `junexis-platform`. Każdy natywny wykonawca
zapisuje własny rachunek `junexis.laboratory-equivalence/0.1`. Windows x64 nie
może zastąpić dowodu Linux x64 ani macOS ARM64; kompilacja krzyżowa potwierdza
jedynie zawartość paczek. Pełna macierz otrzyma status `VERIFIED` po porównaniu
trzech natywnych rachunków.

## Wynik natywny

**Status:** `IMPLEMENTED`, natywnie zweryfikowano `win-x64` i `linux-x64`
(2026-09-11).

Obie próby otrzymały ten sam Build ID oraz identyczne odciski topologii i
zachowania:

- Build ID: `2793f4aa297ab2301e8e517da55f2ff571e11836d4d5d928ce7c4361419a1915`;
- topologia: `12cec2fff00af3e1cbedc79319e8d412783bc42c9845f4b2b0b2506d7c7262e3`;
- zachowanie: `b1bc0165b6279ffeff56b5ada50a188106d67a77811ca8668f5446feae03954b`.

Próba linuksowa użyła samodzielnej paczki `junexis`, przeniesionej kopii
projektu i nie odwoływała się do checkoutu kompilatora. Do zamknięcia pełnej
macierzy pozostaje natywna próba na macOS ARM64.

Rachunki znajdują się w:

- `junexis-platform/evidence/m19-7-3/win-x64/junexis.laboratory-equivalence.json`;
- `junexis-platform/evidence/m19-7-3/linux-x64/junexis.laboratory-equivalence.json`.
