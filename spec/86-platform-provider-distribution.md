# M16.8 — dystrybucja dostawców Storage Runtime

## Granica komponentów

Memory, SQLite i PostgreSQL są trzema niezależnymi komponentami roli
`provider`. Każdy implementuje `junexis.provider/0.2`, zależy od `sdk` oraz
`runtime` i ma osobny artefakt. SDK nie może przejąć własności sterownika bazy.

Wersja dostawcy `0.1.0` jest niezależna od wersji kandydata całej platformy.
Pole `sdk.version` manifestu dostawcy zachowuje wersję kontraktu `0.1.0`.
Dokładną wersję paczki SDK użytej podczas budowania zapisuje osobny rachunek
wydania.

## Rachunek `junexis.provider.release/0.1`

Rachunek zawiera:

- wersję platformy;
- identyfikator i wersję dostawcy;
- SHA-256 manifestu `junexis.provider.json`;
- identyfikator, wersję paczki i ABI SDK;
- repozytorium, rewizję oraz stan źródła;
- zamknięty inwentarz wszystkich pozostałych plików z rozmiarem i SHA-256.

Walidator musi odrzucić niebezpieczną ścieżkę, brak albo nadmiar pliku, zmianę
sumy, obcą wersję platformy i niezgodne ABI.

## Minimalny dowód wykonania

Każdy zestaw musi przejść walidację manifestu publicznym narzędziem SDK oraz
załadować zadeklarowany typ fabryki w czystym konsumencie korzystającym z
abstrakcji SDK tego samego wydania platformy.

Memory i SQLite mogą mieć pełny lokalny dowód bez usług zewnętrznych.
PostgreSQL zachowuje rozdzielenie dwóch poziomów: fabryka, sterownik i kontrakt
są sprawdzane zawsze, natomiast scenariusz live wymaga jawnie dostarczonego
serwera i sekretu. Brak serwera nie może być przedstawiany jako wykonany test
integracyjny.

## Granica instalacji

Stan `available` oznacza gotowy artefakt, nie instalację. Zestawy mają
`selfContained=false`, ponieważ Host Runtime i abstrakcje SDK są osobnymi
komponentami platformy. Instalacja, wybór wersji i powrót do poprzedniej wersji
pozostają osobnym etapem.
