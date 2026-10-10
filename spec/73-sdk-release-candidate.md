# M15.10 — lokalny kandydat wydania SDK

M15.10 ustanawia format `junexis.sdk.release/0.1` dla lokalnego zestawu wydania
SDK. Manifest wiąże wspólną wersję z dokładnym zestawem paczek i sumą SHA-256
każdej z nich. Polecenie:

```text
junexis sdk verify release <katalog>
```

sprawdza manifest, brak nadmiarowych `.nupkg`, nazwy plików, sumy oraz ID i
wersję z wewnętrznego `.nuspec`. Weryfikacja nie ładuje bibliotek z badanego
zestawu. Raport `junexis.sdk.release-verification/0.1` ma status `valid` albo
`invalid`; niezgodność wydania kończy się kodem `6`.

Bramka instaluje narzędzie SDK do izolowanego katalogu i buduje czystego
konsumenta `Junexis.Storage.Runtime.Abstractions` wyłącznie ze źródła paczek
kandydata. Zmienione bajty paczki oraz niezadeklarowany artefakt muszą zostać
odrzucone.

Jest to dowód kompletności i integralności względem manifestu, nie dowód
autentyczności. Publiczne wydanie nadal wymaga podpisu, trwałego rejestru,
opisu pochodzenia artefaktów i zweryfikowanej odtwarzalności.
