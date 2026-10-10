# M16.10 — rozwiązywanie dostawcy projektu

## Decyzja

Projekt korzystający z profilu M16.9 deklaruje `providerSdkVersion`. Każde
powiązanie `[storage.*]` nadal wskazuje logiczną deklarację, identyfikator
dostawcy, wersję dostawcy oraz konfigurację. Mechanizm rozpoznawania wymaga dokładnego
dopasowania trzech wartości:

```text
identyfikator dostawcy + wersja dostawcy + wersja paczki SDK
```

Brak dopasowania jest błędem `doctor` i `build`. Mechanizm nie wybiera
najbliższej ani najnowszej wersji.

## Zgodność

`providerPackages` pozostaje trybem lokalnych paczek projektu z wcześniejszych
etapów. `providerSdkVersion` wybiera profil użytkownika. Manifest nie może
deklarować obu mechanizmów jednocześnie.

Aktualny manifest `junexis.provider/0.2` jest wspierany bez konwersji do
historycznego TOML `0.1`. Lock zachowuje wersję schematu i logiczne źródło
`profile:`, ale nie fizyczną ścieżkę użytkownika.

## Granica

M16.10 rozwiązuje i blokuje paczkę dla procesu budowania. Nie definiuje jeszcze
automatycznego pobierania brakującego dostawcy ani adapterów kolejnych języków
wykonawczych.
