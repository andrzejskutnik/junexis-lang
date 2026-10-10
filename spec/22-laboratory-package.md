# Samodzielna paczka Laboratorium Junexis 0.1

Status: **Experimental — paczki dla trzech platform, natywna próba Windows x64**
Data weryfikacji: **2026-09-09**

Paczka laboratorium jest produktem dla uczestnika kursu, a nie archiwum kodu
źródłowego. Pozwala sprawdzić i uruchomić gotowy system poleceniem `junexis`
bez checkoutu repozytoriów platformy, SDK .NET ani kompilatora Go.

## Polecenia

```text
junexis lab package [projekt] [-o <paczka>] [--runtime win-x64|linux-x64|osx-arm64]
junexis lab verify <paczka> [--json]
junexis lab run <paczka> [--duration <sekundy>]
```

`package` jest poleceniem autora laboratorium. Wykonuje analysis preflight,
natywną bramkę targetu `server`, build związany z Build ID, tworzy i sprawdza
System Bundle, a następnie buduje samodzielny serwer dla wskazanego środowiska.
Bez `--runtime` wybiera bieżący system. Macierz M19 obejmuje `win-x64`,
`linux-x64` oraz `osx-arm64`; inna wartość jest odrzucana jawnie.

`verify` jest operacją offline. Sprawdza wersję schematu, bezpieczne ścieżki,
dokładne pokrycie zawartości manifestem i SHA-256 każdego pliku.

`run` zawsze rozpoczyna od tej samej weryfikacji. Rozpakowuje paczkę do
własnego katalogu tymczasowego, przekazuje serwerowi Build ID i Bundle ID,
czeka na odpowiedź interfejsu WWW dołączonego do demonstratora, a po
zatrzymaniu usuwa własne pliki tymczasowe. Ten interfejs nie jest osobnym
konstruktem języka ani ogólnym komponentem platformy.

## Zawartość 0.1

```text
smart-home-preview-1-<runtime>.junexis-lab.zip
├── laboratory-package.json
├── laboratory.json
├── README.md
├── bin/junexis-laboratory[.exe]
└── evidence/system-bundle.zip
```

Manifest `junexis.laboratory-package/0.1` zawiera tożsamość laboratorium,
wersję, Runtime Identifier, ścieżkę programu i panelu, Build ID, Bundle ID oraz
sumy SHA-256 czterech plików payloadu. System Bundle zachowuje wygenerowane
artefakty i kontrakty, dlatego wykonywalna aplikacja pozostaje związana z
dowodem builda.

## Granice

- paczki `linux-x64` i `osx-arm64` są kompilowane krzyżowo i sprawdzane
  strukturalnie; pełny dowód natywnego wykonania wymaga odpowiednio Linux x64
  oraz macOS ARM64,
- zweryfikowany profil natywnego wykonania to obecnie `win-x64`,
- paczka nie jest jeszcze podpisanym publicznym wydaniem,
- `--trace-url` może dołączyć zewnętrzny TraceCollector; bez niego dom działa
  w jawnym trybie fail-open,
- publikacja pod trwałym adresem i obsługa pobierania należą do kanału wydań,
  nie do kontraktu ZIP.

Pierwsza weryfikacja E2E utworzyła paczkę domu, potwierdziła cztery sumy,
uruchomiła natywny serwer na alternatywnych portach, otrzymała odpowiedź
interfejsu demonstratora i zaobserwowała normalną decyzję, włączenie
wentylacji oraz bezpieczne zachowanie po utracie czujnika.
