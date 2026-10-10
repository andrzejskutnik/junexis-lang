# M16.5 — dystrybucja rozszerzenia VS Code

## Cel

Rozszerzenie VS Code jest samodzielnym, wersjonowanym artefaktem lokalnego
wydania, a nie rezultatem ręcznego pakowania repozytorium narzędziowego.

## Artefakt

```text
junexislang-<wersja-platformy>.vsix
```

Tożsamość rozszerzenia to `junexis-lang.junexislang`. Wersja w
`extension/package.json` oraz `extension.vsixmanifest` musi być dokładnie
wersją wydania platformy. Dla wersji zapowiadającej VSIX musi mieć znacznik
wydania zapowiadającego.

## Podział odpowiedzialności

VSIX zawiera klienta edytora, gramatykę, fragmenty kodu, ikony i zbudowany punkt
wejścia. Nie zawiera LSP. Komponent `vscode-extension` ma w manifeście platformy
jawną zależność `requires=["lsp"]`, a Instalator dodaje LSP przed rozszerzeniem
do planu `junexis install --extension`.

Ten podział ustanawia jedną instalację serwera językowego używaną przez Studio
i VS Code. Nie usuwa narzędziowego wariantu VSIX z dołączonym runtime dla
szczególnych scenariuszy przenośnych.

## Walidacja

Przed publikacją i przed instalacją sprawdzane są:

1. bezpieczne, niepowtórzone ścieżki archiwum;
2. tożsamość i wersja w obu manifestach;
3. wymagany punkt wejścia oraz zasoby języka;
4. deklaracja ustawienia `junexis.lspPath`;
5. brak katalogu `extension/runtime` z drugą kopią LSP.

Manifest `junexis.platform-release/0.1` może oznaczyć rozszerzenie jako
`available` i `verified` dopiero po tej kontroli. Artefakt nie jest
`selfContained`, ponieważ wymaga VS Code i komponentu LSP.

## Granica

M16.5 obejmuje lokalny VSIX Windows x64 oraz instalację przez dostępne lokalnie
polecenie `code`. Publikacja w Marketplace, podpis VSIX, trwały adres pobierania
i instalacja sieciowa pozostają poza etapem.
