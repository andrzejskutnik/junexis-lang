# M16.1 — kontrakt wydania platformy

## Problem

Junexis składa się z aplikacji, narzędzi, bibliotek i dostawców. Lista nazw z
jednym numerem wersji nie dowodzi, że wszystkie te elementy można pobrać ani że
pasują do wybranego systemu. Kontrakt wydania musi odróżnić stan faktyczny od
planu następnych etapów.

## Właściciel

Normatywne znaczenie kontraktu utrzymuje specyfikacja. Schemat, generator i
walidator należą do `junexis-platform`, ponieważ manifest opisuje przekrój przez
wiele repozytoriów. Instalator, portal i centrum pobierania są jego konsumentami.

## Identyfikacja

Wersja schematu:

```text
junexis.platform-release/0.1
```

Manifest dotyczy jednego numeru wydania, jednego kanału oraz jednego połączenia
systemu, architektury i RID. `compatibilitySet` wiąże komponenty w jeden zestaw;
pełna semantyka zgodności wersji zostanie domknięta w M16.2.

## Stany komponentu

### `available`

Komponent ma samodzielny artefakt leżący obok manifestu. Deklaracja zawiera
nazwę pliku, typ mediów, rozmiar, SHA-256 i opcjonalny adres HTTPS. Plik musi
istnieć i odpowiadać deklaracji.

### `embedded`

Komponent jest dostarczony wewnątrz artefaktu innego komponentu. Deklaracja
wskazuje identyfikator właściciela oraz względną, bezpieczną ścieżkę osadzenia.
Nie tworzy drugiego pozornego pliku do pobrania.

### `deferred`

Komponent należy do modelu platformy, ale nie do danego zestawu. Musi mieć
uzasadnienie. Nie może mieć artefaktu, punktu wejścia ani oznaczeń
`selfContained=true` i `verified=true`.

## `required` a dostępność

`required=true` mówi, że komponent jest potrzebny w docelowej klasie wydania.
Nie mówi, że już został spakowany. Pozwala to wykazać lukę zamiast usuwać rolę z
manifestu albo przedstawiać ją jako gotową. Osobna polityka gotowości wydania
określi, czy kanał `preview` lub `stable` może zawierać odroczony komponent
wymagany.

## Zależności i integralność

Każda zależność wskazuje istniejący, inny komponent. Graf musi być acykliczny.
Identyfikatory i pliki są unikalne. Ścieżki absolutne oraz segment `..` są
zabronione. Katalog zestawu jest zamknięty: obecność niezadeklarowanego pliku
oznacza błąd.

## Stany wydania

- `local-candidate` — zestaw istnieje lokalnie i nie ma publicznego adresu;
- `published` — zestaw został opublikowany przez dalszy proces M16;
- `withdrawn` — zestaw nie powinien być proponowany nowym użytkownikom.

Sam stan `published` nie zastępuje podpisu ani polityki zaufania. Powiązanie
manifestu całej platformy z łańcuchem zaufania będzie osobnym etapem.

## Dowód M16.1

`junexis-platform/scripts/test-m16-1-platform-release.ps1` tworzy niewielki
zestaw wzorcowy bez budowania aplikacji i uruchamia ścisły walidator. Próby
negatywne obejmują:

1. zmianę bajtów artefaktu;
2. brakującą zależność;
3. powtórzony identyfikator;
4. przejście ścieżką poza katalog;
5. brak powodu odroczenia;
6. udawaną gotowość komponentu odroczonego;
7. cykl zależności;
8. niezadeklarowany plik;
9. niespójność kanału `preview` z SemVer.

M16.1 ustanawia prawdziwą granicę kontraktu. Nie jest jeszcze dowodem, że każdy
komponent platformy ma gotową paczkę.
