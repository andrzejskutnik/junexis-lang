# M12.11 — wersjonowany Storage Runtime SDK

> **Status:** VERIFIED LOCAL FEED

M12.11 zamyka zależność źródłową pomiędzy Runtime a repozytorium providerów.
Autor providera korzysta z opublikowanego kontraktu binarnego tak samo, jak z
każdej innej biblioteki .NET. Nie musi dodawać projektu Runtime do solution ani
znać jego położenia na dysku.

## Dwie paczki, dwie role

| Paczka | Kto jej używa | Odpowiedzialność |
|---|---|---|
| `Junexis.Storage.Runtime.Abstractions` 0.1.0 | kod produkcyjny providera | interfejsy ABI, wartości, wyniki i błędy |
| `Junexis.Storage.Runtime.Host` 0.1.0 | testy procesu i conformance | referencyjne ładowanie i wykonanie providera |

Provider Memory, SQLite lub PostgreSQL nie zależy produkcyjnie od Host. Dzięki
temu sterownik pozostaje biblioteką dla kontraktu Junexis, a nie rozszerzeniem
jednej konkretnej aplikacji serwerowej.

```text
provider source
      │ PackageReference 0.1.0
      ▼
Storage Runtime Abstractions

provider tests
      │ PackageReference 0.1.0
      ▼
Storage Runtime Host ──► Abstractions
```

## Rytuał pracy autora providera

1. dodaj `PackageReference` do `Junexis.Storage.Runtime.Abstractions`;
2. zaimplementuj fabrykę i sesję z ABI `junexis.storage.runtime/0.1`;
3. zadeklaruj entrypoint i capabilities w manifeście providera;
4. w projekcie testowym dodaj `Junexis.Storage.Runtime.Host`;
5. uruchom wspólną bramkę conformance;
6. wydaj provider niezależnie od Runtime, zachowując zgodność wersji ABI.

Zmiana implementacji `@server` z Go na C#, Javę lub Python nie zmienia tego
pakietu providera. Adapter danego środowiska komunikuje się z Runtime ABI;
provider nie jest kompilowany do generatora targetu.

## Lokalny feed i przyszły rejestr

Od M15.2 repo `junexis-sdk` tworzy paczkę Abstractions:

```powershell
.\pack-sdk.ps1
```

Repo `junexis-runtime` tworzy paczkę Host i może złożyć zgodnościowy bundle obu
artefaktów bez przejmowania źródeł SDK:

```powershell
.\pack-storage-sdk.ps1
```

Repo `junexis-storage-providers` przyjmuje jawnie źródło SDK oraz Hosta:

```powershell
.\run-provider-gate.ps1 `
  -SdkPackageSource ..\junexis-sdk\artifacts\storage-sdk `
  -RuntimeHostPackageSource ..\junexis-runtime\artifacts\storage-sdk
```

Lokalny feed jest dzisiejszym kanałem weryfikacji, ponieważ platforma nie ma
jeszcze publicznego hostingu paczek. Docelowy rejestr zastąpi tylko źródło
NuGet. Nazwy pakietów, wersje i `PackageReference` pozostaną bez zmian.

## Dowód M12.11

- solution providerów nie zawiera projektów Runtime;
- żaden projekt providera nie ma cross-repo `ProjectReference`;
- paczka Host dostarcza także `deps.json` i `runtimeconfig.json` przez
  `buildTransitive`, więc można uruchomić ją jako osobny proces;
- izolowany restore korzysta z wersji `0.1.0` z lokalnego feedu;
- bramka providerów: 23 testy PASS, 2 PostgreSQL live SKIP;
- pełna bramka solution Runtime: 553 testy PASS.

Status `VERIFIED LOCAL FEED` nie oznacza publicznego wydania. Przed takim
wydaniem pozostają podpisywanie paczek, trwały adres rejestru, polityka
retencji wersji oraz test instalacji z publicznego źródła.

Aktualną własność i migrację jednego źródła opisuje
[`65-sdk-repository-ownership.md`](65-sdk-repository-ownership.md).
