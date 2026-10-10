# M17.4.3 — rozwiązywanie migawki Registry

## Zakres

`RegistrySnapshotPackageSource` przekształca niezmienny kontrakt
`junexis.registry-snapshot/0.1` do wewnętrznego indeksu istniejącego resolvera.
Nie zmienia manifestu projektu, formatu `junexis.packages.lock`, konfiguracji
CLI ani polityki ponowień transportu.

## Integralność

Źródło odrzuca nieznane pola i typy artefaktów przez zamkniętą deserializację.
Ponownie oblicza `snapshotId` jako SHA-256 kanonicznej tablicy `artifacts`,
sprawdza limit 10 000 wpisów, unikatowość tożsamości, rozmiary oraz spójność
logicznego pola `source`. Wpisy pakietów przechodzą także istniejącą walidację
identyfikatorów, SemVer, SHA-256 i przenośnych ścieżek indeksu.

## Dokładne wersje

Do kolejki mechanizmu odtwarzania trafiają dokładne zależności z migawki. Po
pobraniu ZIP mechanizm porównuje je z manifestem wraz z zakresem języka i
środowiskami docelowymi. Rozbieżność przerywa operację przed zatwierdzeniem
pamięci podręcznej i zapisem pliku blokady.

Artefakt jest pobierany po stabilnej tożsamości:

```text
/v1/artifacts/package/{id}/{version}/content
```

Pole `artifactPath` pozostaje metadanym transportowym migawki i nie jest
traktowane jako zaufany URL. Wpisy dostawców są poprawnie deserializowane, ale
pomijane przez mechanizm odtwarzania pakietów aplikacyjnych.

## Weryfikacja

Testy potwierdzają bajtowo identyczny plik blokady źródła lokalnego i Registry,
odrzucenie błędnego `snapshotId`, odrzucenie rozbieżnych zależności bez zmiany
pamięci podręcznej oraz pominięcie wpisów dostawców.
