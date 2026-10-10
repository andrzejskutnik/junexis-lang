# M15.8 — oficjalny provider jest zewnętrznym konsumentem SDK

M15.8 usuwa wyjątek, w którym referencyjne providery Junexis miały słabszą
granicę zależności niż provider wygenerowany dla zewnętrznego autora.

Manifest `junexis.provider/0.2` jawnie opisuje:

- runtime ID, package ID oraz factory type;
- względną ścieżkę produkcyjnego `.csproj`;
- assembly i TFM;
- ABI, SDK oraz capabilities;
- entrypoint, driver, pliki paczki i klasyfikację konfiguracji.

Memory, SQLite i PostgreSQL używają tego JSON jako jednego źródła metadanych
autora i dystrybucji. Poprzedni manifest TOML został usunięty. Wspólne kernele
Query Plan i Mutation Plan są nadal implementacją providerów, lecz produkcyjne
projekty konsumują je jako paczki 0.1.0 zamiast `ProjectReference`.

Fixture conformance przyjmuje bezpieczne odwołanie `${env:NAME}`. Narzędzie
rozwiązuje całą wartość dopiero przed otwarciem bindingu. Diagnostyka ujawnia
wyłącznie nazwę brakującej zmiennej, nigdy sekret.

Bramka M15.8 dowodzi walidacji wszystkich trzech manifestów, rzeczywistego
Conformance Kitu Memory i SQLite oraz bezpiecznego zatrzymania PostgreSQL bez
sekretu. Live profil Storage Plans wszystkich trzech providerów jest zakresem
M15.9.
