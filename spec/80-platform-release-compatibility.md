# M16.2 — zgodność wydania platformy

## Cel

Manifest `junexis.platform-release/0.1` opisuje faktyczną dostępność i
integralność składników. M16.2 dodaje osobny kontrakt, który rozstrzyga, czy
wersje tych składników, ich role, kontrakty i zależności tworzą zatwierdzoną
rodzinę platformy.

## Kontrakt

Katalog ma wersję:

```text
junexis.platform-compatibility/0.1
```

Zawiera:

- stabilny identyfikator rodziny zgodności;
- kanał wydania;
- przedział wersji całego wydania;
- obsługiwane środowiska docelowe identyfikowane przez RID;
- dokładny zbiór komponentów;
- dla każdego komponentu: rolę, wymagalność, przedział wersji, dopuszczalne
  stany, kontrakt oraz zależności.

## Semantyka przedziału

Przedział jest półotwarty:

```text
[minimumInclusive, maximumExclusive)
```

Porównanie jest zgodne z SemVer 2.0.0. Wersja bez oznaczenia wstępnego ma
wyższy priorytet niż wersja wstępna o tej samej części głównej. Identyfikatory
numeryczne wersji wstępnej są porównywane liczbowo i mają niższy priorytet od
identyfikatorów nienumerycznych.

## Relacja do manifestu M16.1

`compatibilitySet` manifestu jest kluczem obcym do `id` katalogu. Resolver
wymaga dokładnej zgodności tego identyfikatora, kanału i środowiska. Manifest nie
może pominąć reguły katalogu ani dodać nieznanego komponentu.

Stan dostępności i zgodność pozostają niezależne:

- M16.1 dowodzi, czy komponent ma artefakt, jest osadzony albo odroczony;
- M16.2 dowodzi, czy jego deklaracja należy do zatwierdzonej rodziny;
- późniejsza polityka publikacji zdecyduje, czy zestaw jest dostatecznie
  kompletny dla danego kanału.

## Pierwszy katalog

`junexis-0.1-preview` obejmuje pełny inwentarz trzynastu komponentów na
`win-x64` w zakresie od `0.1.0-preview.1` do `0.2.0` wyłącznie. Utrwala między
innymi:

- `junexis.sdk.release/0.1` dla SDK;
- `junexis.provider/0.2` dla Memory, SQLite i PostgreSQL;
- `junexis.laboratory-package/0.1` dla Laboratorium;
- zależność dostawców od SDK i środowiska wykonawczego;
- zależność rozszerzenia VS Code od LSP;
- zależność Laboratorium od Instalatora i kompilatora.

## Dowód

`junexis-platform/scripts/test-m16-2-platform-compatibility.ps1` weryfikuje
wariant pozytywny i dziesięć prób negatywnych. Generator Windows korzysta z
katalogu odpowiadającego kanałowi i zatrzymuje wydanie, gdy resolver wykryje
niezgodność.

M16.2 nie publikuje jeszcze paczek i nie ustanawia oficjalnego kanału `stable`.
