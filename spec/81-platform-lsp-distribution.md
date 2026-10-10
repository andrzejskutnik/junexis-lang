# M16.3 — dystrybucja LSP

## Cel

Komponent LSP ma być rzeczywistym składnikiem lokalnego wydania platformy, a
nie programem dostępnym wyłącznie po zbudowaniu repozytorium języka.

## Artefakt

Dla Windows x64 powstaje:

```text
junexis-lsp-<wersja>-win-x64.zip
```

Archiwum zawiera samodzielną publikację ReadyToRun, punkt wejścia
`JunexisLang.Compiler.LspHost.exe` oraz `junexis-lsp.json`. Manifest platformy
opisuje ten ZIP jako `available`, `selfContained=true` i `verified=true`.

## Kontrakt wewnętrzny

`junexis-lsp.json` ma zamknięty zestaw pól:

- `schemaVersion` równe `1`;
- `name` równe `JunexisLang.Compiler.LspHost`;
- `version` identyczne z wersją wydania;
- dodatnie `protocolVersion`;
- `runtimeIdentifier` równe `win-x64`;
- bezpieczną, względną nazwę `entrypoint`;
- `readyToRun=true`;
- `selfContained=true`.

## Dowód wykonania

Walidacja uruchamia punkt wejścia z `--version`. Zwrócone `name`, `version` i
`protocolVersion` muszą być identyczne z manifestem. Tym samym dowód obejmuje
nie tylko strukturę katalogu, lecz również zdolność uruchomienia opublikowanego
programu.

## Bramka negatywna

`junexis-platform/scripts/test-m16-3-lsp-distribution.ps1` odrzuca inną wersję,
brak samodzielności, przejście ścieżką poza paczkę, obcą tożsamość i brak EXE.
Próba pozytywna wykonuje prawdziwe `dotnet publish` dla `win-x64`.

## Poza zakresem

M16.3 nie definiuje miejsca instalacji, aktualizacji ani wyboru LSP przez Studio
i rozszerzenie VS Code. Te elementy mogą korzystać z gotowego artefaktu w
następnym etapie.
