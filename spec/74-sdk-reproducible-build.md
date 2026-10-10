# M15.11 — odtwarzalność i pochodzenie artefaktów SDK

M15.11 dowodzi bitowej odtwarzalności lokalnego zestawu wydania SDK dla tej
samej rewizji, konfiguracji, platformy i wersji .NET SDK. Proces normalizuje
losowe elementy kontenera NuGet: kolejność i czas wpisów ZIP, ścieżkę dokumentu
właściwości podstawowych oraz identyfikatory relacji. SHA-256 jest obliczane
dopiero po normalizacji.

Zestaw zawiera plik `junexis.sdk.provenance.json` w wersji
`junexis.sdk.provenance/0.1`. Dokument wiąże manifest wydania z rewizją Git,
stanem `clean` (czyste) lub `dirty` (zmienione), konfiguracją i zestawem
narzędzi. Polecenie:

```text
junexis sdk verify provenance <katalog>
```

sprawdza ten związek bez ładowania bibliotek z badanego zestawu. Raport
`junexis.sdk.provenance-verification/0.1` ma kod niezgodności `7` i osobne pole
`sourceReady`; czyste źródło nie oznacza automatycznie gotowości publikacji.

Bramka wymaga identycznych bajtów dwóch procesów budowania oraz odrzucenia
informacji o pochodzeniu po jakiejkolwiek zmianie związanego manifestu. Zakres
nie obejmuje jeszcze dowodu między systemami operacyjnymi ani kryptograficznego
podpisu tej informacji.
