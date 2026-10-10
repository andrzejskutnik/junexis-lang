# M16.4 — instalacja LSP

## Polecenie użytkownika

```text
junexis install --lsp --local-packages <katalog-wydania>
```

Instalator wybiera `junexis-lsp-<wersja-platformy>-win-x64.zip`. Wersja nie jest
stałą w kodzie: pochodzi z tożsamości pliku wykonywalnego Installera danego wydania.

## Położenie

Aktywna instalacja Windows znajduje się w:

```text
%LOCALAPPDATA%\Junexis\lsp
```

Odpowiedniki kontraktu to `~/Library/Application Support/Junexis/lsp` na macOS
i `~/.local/share/junexis/lsp` na Linux. Bieżący artefakt pozostaje ograniczony
do Windows x64.

## Transakcja

Instalator:

1. rozpakowuje ZIP do unikalnego katalogu roboczego;
2. sprawdza `junexis-lsp.json`, wersję, RID, ReadyToRun i samodzielność;
3. uruchamia punkt wejścia z `--version`;
4. porównuje nazwę, wersję i protokół z manifestem;
5. przenosi dotychczasowy katalog do kopii;
6. atomowo aktywuje katalog roboczy;
7. usuwa kopię dopiero po sukcesie.

Wyjątek przed aktywacją usuwa katalog roboczy. Błąd podmiany przywraca kopię poprzedniej
wersji.

## Klienci

Studio wybiera jawną wartość `JUNEXIS_LSP`, następnie instalację użytkownika,
wersję zbudowaną ze źródeł i awaryjne `junexis lsp`. Rozszerzenie VS Code zachowuje swoje
ustawienie i dystrybucję dołączoną do VSIX, ale rozpoznaje również standardową
instalację oraz jej manifest protokołu.

## Dowód

`junexis-platform/scripts/test-m16-4-lsp-installation.ps1` publikuje rzeczywisty
samodzielny LSP, instaluje go przez `AppInstaller` do izolowanego profilu,
uruchamia z miejsca docelowego i wykonuje testy wykrywania Studio oraz VS Code.

M16.4 nie ustanawia publicznego adresu pobierania ani automatycznej aktualizacji
z sieci.
