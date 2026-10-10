# M16.7 — dystrybucja SDK

## Kontrakt komponentu

Komponent `sdk` wydania platformy ma artefakt ZIP zgodny z
`junexis.sdk.release/0.1`. Wersja manifestu SDK oraz obu paczek NuGet musi być
identyczna z wersją platformy. SDK zależy od komponentu `runtime`, ale nie
zawiera jego kopii.

Zestaw składa się z:

- `Junexis.Storage.Runtime.Abstractions` — neutralnego ABI dla dostawców;
- `Junexis.SDK.Tool` — narzędzia autora instalowanego jako narzędzie .NET;
- manifestu wydania z sumami SHA-256;
- informacji o pochodzeniu związanej z manifestem i rewizją źródła.

Obie paczki muszą mieć dokładnie tę samą wersję wydania. Narzędzie jest paczką
typu `PackAsTool`; dowodem jego kompletności jest instalacja i wykonanie w
czystym profilu. Zamknięty inwentarz nie pozwala dołączyć niezadeklarowanej
paczki.

## Znaczenie stanu `available`

Stan `available` oznacza, że zestaw istnieje, przechodzi niezależny walidator,
daje się zainstalować jako narzędzie .NET i ma działającego, czystego
konsumenta kontraktu. Nie oznacza instalacji w profilu użytkownika, publikacji
w publicznym rejestrze ani oficjalnego podpisu projektu.

Komponent zachowuje `selfContained=false`. Publicznym modelem polecenia jest
`junexis sdk`; nazwa `junexis-sdk` pozostaje technicznym punktem wejścia paczki
narzędziowej, a nie drugim interfejsem całej platformy.

## Granica zaufania

Manifest i informacja o pochodzeniu są objęte kontrolą integralności całego
wydania platformy. M16.7 nie generuje krótkotrwałego klucza i nie przedstawia
go jako oficjalnego. Podpisane wydanie wymaga osobnego, chronionego procesu
publikacji oraz zewnętrznej polityki zaufania opisanej w M15.12–M15.14.
