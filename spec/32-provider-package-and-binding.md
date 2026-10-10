# M12.2 — pakiet providera, binding i lockfile

> **Status:** VERIFIED dla lokalnych pakietów projektu
>
> **Schemat pakietu:** `junexis.provider/0.1`
>
> **Schemat blokady:** `junexis.providers.lock/0.1`
>
> **Runtime ABI:** zadeklarowane `junexis.storage.runtime/0.1`, implementacja w M12.3

## Zakres

M12.2 ustanawia tożsamość providera przed uruchomieniem jego kodu. Projekt może
wskazać lokalny pakiet, jawnie związać deklarację storage z jego dokładną
wersją i otrzymać deterministyczny lockfile z SHA-256. Rejestr zdalny,
instalacja i wykonanie runtime pozostają poza tym etapem.

## Manifest pakietu

Każdy pakiet zawiera `junexis.provider.toml`:

```toml
schemaVersion = "junexis.provider/0.1"
id = "postgresql"
version = "0.1.0"
storageContract = "junexis.storage/0.1"
runtimeAbi = "junexis.storage.runtime/0.1"
resourceKind = "relational"
databaseCompatibility = ">=16 <17"
capabilities = ["records", "transactions", "migrations", "native-sql"]
files = ["schemas/postgresql.json"]

[runtime]
kind = "dotnet"
entrypoint = "runtime/Junexis.Provider.Postgresql.dll"

[driver]
id = "npgsql"
version = "10.0.0"

[configuration]
required = ["connection"]
optional = ["poolSize", "operationTimeout"]
secret = ["connection"]
```

Wersja providera jest semantyczna. ID używa małych liter, cyfr, kropek i
myślników. `storageContract` oraz `runtimeAbi` muszą być zgodne z wersjami
obsługiwanymi przez narzędzia.

`files` jest zamkniętą listą plików wpływających na tożsamość pakietu.
Entrypoint runtime jest dołączany zawsze, również jeśli nie występuje w tej
liście. Resolver odrzuca brak pliku, ścieżkę absolutną i próbę wyjścia poza
katalog pakietu.

## Binding projektu

Projekt wskazuje względne katalogi pakietów oraz bindingi:

```toml
providerPackages = ["providers/postgresql"]

[storage.home]
declaration = "HomeStore"
provider = "postgresql"
providerVersion = "0.1.0"
config.connection = "secret:HOME_DATABASE_URL"
config.poolSize = "10"
```

Nazwa po `storage.` jest stabilną nazwą bindingu. `declaration` wskazuje
osiągalny kontrakt `extern storage` okresu bootstrapowego. Build zatrzymuje się
z `B0006`, jeśli deklaracja nie istnieje, oraz `B0007`, jeśli po włączeniu
jawnych bindingów deklaracja storage pozostała bez bindingu.

M12.2 obsługuje trzy źródła konfiguracji:

| Zapis | Znaczenie | Lockfile |
|---|---|---|
| `secret:NAME` | odwołanie do sekretu deploymentu | rodzaj i nazwa referencji, bez wartości |
| `env:NAME` | odwołanie do zmiennej środowiska | rodzaj i nazwa referencji, bez wartości |
| `literal:value` albo `value` | jawna wartość niesekretna | wartość jest zapisana |

Klucz oznaczony przez provider jako `secret` nie może otrzymać literału.
Komunikat błędu nie powtarza odrzuconej wartości.

## Rozstrzyganie

Resolver wykonuje kolejno:

1. sprawdzenie, że ścieżka pakietu pozostaje wewnątrz projektu,
2. parsowanie i walidację manifestu providera,
3. sprawdzenie unikalności ID,
4. dopasowanie ID oraz dokładnej wersji do bindingu,
5. kontrolę wymaganych, opcjonalnych i nieznanych ustawień,
6. ochronę kluczy sekretnych,
7. hash manifestu i każdego deklarowanego pliku,
8. hash całego pakietu z kanonicznej, uporządkowanej listy,
9. powiązanie bindingu z osiągalną deklaracją storage podczas builda.

Nie ma automatycznego wyboru podobnej wersji ani providera o zbliżonej nazwie.

## Lockfile

Udany build zapisuje:

```text
out/debug/junexis.providers.lock.json
```

Lockfile zawiera:

- schemat `junexis.providers.lock/0.1`,
- ID i dokładną wersję providera,
- wersje kontraktu storage oraz runtime ABI,
- resource kind i database compatibility,
- uporządkowane capabilities,
- runtime i driver,
- względne źródło pakietu,
- SHA-256 manifestu, plików i całego pakietu,
- binding, deklarację i dokładny hash pakietu,
- źródła konfiguracji bez wartości sekretów.

Kolejność providerów, bindingów, capabilities, ustawień i plików jest
deterministyczna. Powtórny build niezmienionego projektu tworzy identyczną
treść lockfile.

## Wykonanie od M12.8

`junexis run` ponownie oblicza pakiet i porównuje wynik z lockfile bezpośrednio
przed startem. Dopiero po zgodności rozwiązuje referencje środowiskowe i
sekretne. `junexis package` kopiuje te same zweryfikowane pliki do System Bundle
i obejmuje je jego manifestem integralności.

## Granice

- lokalne pakiety muszą znajdować się wewnątrz projektu,
- capabilities są deklarowane i blokowane, ale dopasowanie do operacji będzie
  rozszerzone wraz z ABI,
- nie istnieje rejestr ani podpis pakietu,
- lockfile dowodzi integralności lokalnego wejścia, nie zaufania do wydawcy,
- baseline launchera obsługuje jeden binding storage na proces serwera.

## Dowód

Testy CLI potwierdzają:

- odczyt sekcji `[storage.*]`,
- deterministyczny lockfile,
- zmianę hasha po zmianie pliku pakietu,
- odrzucenie ścieżki wychodzącej poza projekt,
- odrzucenie jawnej wartości dla sekretnego klucza bez ujawnienia jej treści,
- zatrzymanie builda przy nieistniejącej deklaracji,
- zapis blokady przy poprawnym bindingu oraz identyczny wynik kolejnego builda.
