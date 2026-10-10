# Golden Modular Storage E2E

> **Identyfikator:** `junexis.golden-modular-storage/0.1`
>
> **Status:** VERIFIED
>
> **Kamień milowy:** M11.10

## Teza dowodu

M11.10 odpowiada na jedno praktyczne pytanie: czy system zbudowany z wielu
modułów Junexis potrafi zapisać dane przez neutralny kontrakt storage,
przetrwać restart procesu i dostarczyć odtworzony stan do panelu przez zadanie
serwera?

Odpowiedź jest twierdząca dla referencyjnego scenariusza
`examples/projects/12-modular-storage-golden`.

## Granice

```text
pięć modułów .jnx
        │
        ▼
zadania domenowe @server
        │
        ▼
extern storage HomeStore
        │
        ▼
junexis.storage.Store ── SQLite / memory
        │
        ▼
HomeSnapshot @server ── chan ── HomeStoragePanel @report
```

Panel nie zna ścieżki bazy, SQL ani providera. Dane docierają do niego jako
typowany `HomeSnapshot` wysłany przez `PublishHomeSnapshot @server`. Endpoint
REST odczytuje stan przez `ReadHomeSnapshot`, czyli tę samą granicę domenową.

## Co zostało wykonane

- kompozycja pięciu osiągalnych modułów `.jnx`,
- wygenerowanie targetów `server` i `report`,
- migracja `001-storage.sql` zabezpieczona mechanizmem migracji providera,
- zapis obserwacji przez REST i adapter `HomeStore`,
- kontrolowany rollback bez widocznego skutku,
- zakończenie pierwszego procesu i uruchomienie drugiego na tej samej bazie,
- odczyt zatwierdzonej obserwacji przez API serwera,
- potwierdzenie ścieżki `PublishHomeSnapshot -> HomeStoragePanel`,
- zapis providera, listy modułów, migracji oraz identyfikatorów próby.

Próba zakończyła się `PASS` na Windows x64. Natywny serwer Go działa bez CGO i
bez kompilatora C. Provider `memory` realizuje ten sam `storage.Store`, lecz
zgodnie z kontraktem nie zachowuje danych po restarcie procesu.

## Znaleziona i naprawiona luka

Golden E2E ujawnił, że `analyze` i natywna bramka artefaktów sprawdzały jedynie
plik wejściowy, mimo że `check` i `build` komponowały cały graf projektu.
Powodowało to fałszywe błędy nieznanych tasków w pipeline i protokole podczas
`junexis package`.

Oba etapy korzystają teraz z `CliProjectChecker`, więc analiza, bramka i build
widzą ten sam złożony program. Testy regresyjne obejmują topologię, protokół i
natywną bramkę projektu z importowanym taskiem.

## Dowód System Bundle

Końcowa próba utworzyła i zweryfikowała `junexis.system-bundle/0.1` z targetami
`server` i `report`. Z paczki wyodrębniono do katalogu tymczasowego wyłącznie
artefakty runtime. Następnie:

1. natywny `junexis-server.exe` osiągnął gotowość bez checkoutu i kompilacji,
2. aplikacja raportowa osiągnęła gotowość i podała zbudowany `dist/browser.js`,
3. serwer zapisał obserwację do SQLite,
4. proces serwera został zakończony i uruchomiony ponownie z tej samej paczki,
5. nowy proces odtworzył rekord oraz zachował Build ID i Bundle ID.

Zweryfikowany Build ID to
`345445370763cd03cf81fa9cc976fdc79c393f02c3b0352656f208d98f6b32fd`, a Bundle
ID to `4bb3b2db62dccd9671ae187385ebc3badb98c62eac09a5779c3308da4fe5a63f`.
Manifest integralności obejmuje 39 plików. Bundle nie ma jeszcze podpisu;
został jawnie zaakceptowany przez politykę permissive. Podpisane wydanie jest
etapem dystrybucji, nie brakującym kryterium M11.
