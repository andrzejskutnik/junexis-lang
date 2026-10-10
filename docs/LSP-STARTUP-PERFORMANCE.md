# LspHost startup performance

## Cel

Pomiar obejmuje czas od utworzenia procesu `JunexisLang.Compiler.LspHost` do
odebrania pełnej odpowiedzi na żądanie LSP `initialize`. Nie obejmuje otwarcia
pliku, diagnostyk dokumentu ani renderowania UI klienta.

Skrypt pomiarowy:

```powershell
./scripts/measure-lsp-startup.ps1 `
  -LspPath ./path/to/JunexisLang.Compiler.LspHost.exe `
  -Iterations 5 `
  -Warmup 1
```

Parametr `-BudgetMs` kończy skrypt błędem, gdy mediana przekroczy budżet:

```powershell
./scripts/measure-lsp-startup.ps1 `
  -LspPath ./path/to/JunexisLang.Compiler.LspHost.exe `
  -Iterations 5 `
  -Warmup 1 `
  -BudgetMs 700
```

## Publikacja ReadyToRun

Framework-dependent ReadyToRun zachowuje osobne pliki hosta i zależności, czego
wymaga obecny mechanizm uruchamiania oraz shadow-copy w narzędziach Junexis:

```powershell
./scripts/publish-lsp.ps1 -RuntimeIdentifier win-x64
```

Domyślny wynik:

```text
compiler/JunexisLang.Compiler.LspHost/bin/Release/publish-win-x64-r2r/
```

Skrypt obsługuje inne RID-y przez `-RuntimeIdentifier`. Flaga
`-SelfContained` tworzy większy pakiet niewymagający zainstalowanego runtime
.NET.

Publikacja tworzy również `junexis-lsp.json`. Manifest zawiera wersję hosta,
wersję protokołu zgodności, RID, entrypoint i informację o ReadyToRun. Dane
pochodzą z `JunexisLang.Compiler.LspHost --version`, więc pipeline wydania i
sam host korzystają z jednego kontraktu wersji.

## Baseline 2026-07-26

Windows x64, .NET 10, pięć próbek po jednym warmup:

| Wariant | Mediana | p95 | Min–max |
|---|---:|---:|---:|
| Dotychczasowy framework-dependent host | 1002,5 ms | 1091,5 ms | 835,6–1091,5 ms |
| Framework-dependent ReadyToRun composite | 536,2 ms | 584,3 ms | 461,2–584,3 ms |

ReadyToRun skrócił medianę o około 46,5%. Roboczy budżet regresji dla tej
maszyny wynosi 700 ms po jednym warmup.

Wariant self-contained ReadyToRun osiągnął medianę 543,0 ms, p95 654,4 ms
i zakres 448,6–654,4 ms. Jego wydajność jest porównywalna z wariantem
framework-dependent, ale katalog publikacji ma około 107 MB, ponieważ zawiera
runtime .NET.

Pierwsze uruchomienie świeżo opublikowanego pliku osiągnęło jednorazowo
4123,0 ms, podczas gdy następne próbki wynosiły 552,5–606,3 ms. Ten outlier
może obejmować ładowanie stron pliku, skan systemowy i zimne cache. Dlatego
raportujemy osobno doświadczenie first-ever launch i stabilną medianę; warmup
nie może służyć do ukrywania regresji dystrybucji.

## Interpretacja dla IDE

Prewarm w Junexis VS Code extension zmniejsza koszt odczuwany przy pierwszym
otwarciu `.jnx`, ale nie zastępuje optymalizacji procesu. Dystrybucja IDE
powinna preferować opublikowany wariant ReadyToRun zgodny z platformą i wersją
extension/compiler.

Wydanie dla użytkownika końcowego powinno używać `-SelfContained`. Wariant
framework-dependent jest odpowiedni dla developerów i środowisk zarządzanych,
które gwarantują zgodny runtime .NET.

## Macierz dystrybucji CI

Jenkins publikuje na `main` i tagach cztery dystrybucje self-contained
ReadyToRun:

- `win-x64`,
- `linux-x64`,
- `linux-arm64`,
- `osx-arm64`.

Każdy katalog `publish/lsp/<rid>` zawiera host oraz `junexis-lsp.json`.
Równolegle powstaje archiwum `publish/junexis-lsp-<rid>.tar.gz`. Manifest jest
generowany przez host uruchomiony na platformie buildu, dlatego CI może
cross-publikować obce RID-y bez próby uruchamiania ich natywnych entrypointów.
Pipeline `junexis-tooling` konsumuje surowe katalogi jako wersjonowany kontrakt
między repozytoriami i mapuje je na targety `vsce`.
