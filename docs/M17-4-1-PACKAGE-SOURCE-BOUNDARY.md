# M17.4.1 — granica źródła pakietów

Status: **ZWERYFIKOWANO**

## Odpowiedzialność

`PackageRestoreResolver` zależy od wewnętrznego kontraktu `IPackageSource`, który
dostarcza dokument indeksu oraz kopiuje wskazany artefakt do strumienia
roboczego kontrolowanego przez mechanizm odtwarzania.

Po stronie mechanizmu odtwarzania pozostają bez zmian:

- walidacja indeksu, manifestu, rozmiarów i SHA-256;
- algorytm grafu zależności z dokładnymi wersjami;
- transakcja pamięci podręcznej i dziennik transakcji;
- format `junexis.packages.lock/0.1`;
- odtwarzanie bez sieci wyłącznie z pliku blokady i pamięci podręcznej.

## Implementacje źródła

`LocalPackageSource` zachowuje dotychczasowe zachowanie `packageIndex`: indeks i
artefakty są odczytywane z kontrolowanego katalogu lokalnego. Jest to domyślny
wariant używany przez CLI.

`NetworkPackageSource` rozwiązuje URI artefaktu względem URI indeksu i deleguje
transport do `INetworkPackageSourceClient`. Nie implementuje jeszcze HTTP.
Bezpieczny klient z limitami, kontrolą czasu, anulowaniem i kontrolą przekierowań
należy do M17.4.2, a przekształcenie migawki Registry do indeksu mechanizmu do
M17.4.3.

Mechanizm przekazuje token anulowania do obu operacji źródła. Artefakt trafia do
tymczasowego strumienia z limitem 256 MiB, który odrzuca nadmiar w czasie
przesyłania, przed wyczerpaniem miejsca i przed jakimkolwiek zatwierdzeniem pamięci podręcznej.

## Dowód zgodności

Test implementacji źródła odtwarza ten sam projekt ze źródła lokalnego i kontrolowanego
źródła sieciowego. Oba przebiegi tworzą bajtowo identyczny plik blokady i nie
pozostawiają danych roboczych źródła w trwałej pamięci podręcznej. Pełny zestaw
`PackageRestoreTests` potwierdza zachowanie lokalnego pionu M16 oraz pracy bez
sieci. Osobne testy potwierdzają limit zapisu oraz brak zmian pamięci
podręcznej i pliku blokady po anulowaniu transportu.
