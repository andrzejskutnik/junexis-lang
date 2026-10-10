# M19.4.2 — wieloplatformowe narzędzia języka

**Status:** natywnie zweryfikowano Windows x64 i Linux x64; oczekuje macOS arm64

## Cel

Kompilator i serwer LSP mają powstawać z jednego źródła dla trzech wariantów:
`win-x64`, `linux-x64` oraz `osx-arm64`. Użytkownik nie instaluje środowiska
.NET, ponieważ każda paczka jest samodzielna.

To nie są trzy odmiany języka. RID wybiera pliki wykonywalne właściwe dla
systemu, ale nie zmienia znaczenia projektu `.jnx`, diagnostyk ani protokołu
LSP.

## Zawartość paczki

```text
junexis-language-tools-<wersja>-<rid>
├── compiler/
│   ├── program Kompilatora
│   └── junexis-cli.json
└── lsp/
    ├── program serwera LSP
    └── junexis-lsp.json
```

Manifesty wiążą program z wersją, RID-em i wersją protokołu. Rachunek
budowania zapisuje rozmiar oraz SHA-256 całego archiwum.

## Budowanie

```powershell
./scripts/publish-m19-4-2.ps1 `
  -RuntimeIdentifier win-x64 `
  -Version 0.19.0-preview.1
```

Budowanie przeznaczone do publikacji powinno odbywać się na systemie zgodnym z
RID-em. `-AllowCrossBuild` służy wyłącznie do wcześniejszego wykrycia błędów
kompilacji pozostałych wariantów; taki wynik otrzymuje dowód
`cross-build-only` i nie ogłasza wsparcia platformy.

## Bramka jakości

```powershell
./scripts/verify-m19-4-2.ps1 -BuildAll
```

Bramka:

- uruchamia pełne testy całego rozwiązania Kompilatora;
- porównuje RID-y z kontraktem `junexis-sdk`;
- tworzy wszystkie trzy paczki;
- sprawdza manifesty, zawartość, rozmiar i SHA-256;
- wymaga natywnej odpowiedzi Kompilatora i LSP dla bieżącego RID-u;
- nie przedstawia budowania krzyżowego jako próby na docelowym systemie.

## Stan dowodów

Na Windows x64 i Linux x64 oba programy zostały zbudowane i uruchomione.
Natywne rachunki są zapisywane automatycznie w
`evidence/m19-4-2/<rid>.json`. Paczka macOS arm64 buduje się krzyżowo, lecz
pozostaje planowana do czasu próby na natywnym wykonawcy. Dlatego etap jest
zaimplementowany, ale nie ma jeszcze statusu `VERIFIED` dla całej macierzy.
