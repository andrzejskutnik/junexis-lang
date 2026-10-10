# M15.1–M15.2 — niezależna własność Junexis SDK

> **Status:** VERIFIED WORKSPACE MIGRATION
>
> **Pakiet:** `Junexis.Storage.Runtime.Abstractions` 0.1.0

## Decyzja architektoniczna

Publiczne kontrakty dla autorów rozszerzeń nie należą do implementacji
Runtime. Repo `junexis-sdk` jest ich źródłem i wydawcą, podczas gdy Runtime,
providery i przyszłe rozszerzenia są konsumentami wersjonowanych paczek.

```text
junexis-sdk
  publiczne ABI + kontrakty + przyszłe conformance kits
       ▲                                  ▲
       │ PackageReference                 │ PackageReference
junexis-runtime                    junexis-storage-providers
  Host i lifecycle                   sterowniki i kernele wykonawcze
```

## M15.1 — inwentaryzacja

Paczka Abstractions 0.1.0 zawiera 21 publicznych typów: 10 provider ABI, 5
identyfikatorów kontraktowych i 6 pomocników relational authoring / Native SQL.
Inwentaryzacja ustaliła dwóch konsumentów Runtime, pięciu produkcyjnych
konsumentów w repo providerów i trzy projekty testowe zależne od Runtime Host.

Native SQL łączy przenośne nazwy wire z ADO.NET. Jest kandydatem do późniejszej
osobnej paczki, ale jego natychmiastowe usunięcie złamałoby kompatybilność 0.1.

## M15.2 — migracja

Cały istniejący projekt został przeniesiony do `junexis-sdk` bez zmiany
PackageId, wersji, namespace, TFM i powierzchni API. Runtime usunął projekt ze
solution, a Host i testy przeszły z `ProjectReference` na `PackageReference`.
Providery pobierają Abstractions bezpośrednio z feedu SDK, natomiast testowy
Host nadal pochodzi z Runtime.

Runtime może składać zgodnościowy bundle obu paczek, lecz manifest wskazuje ich
różnych właścicieli. Nie tworzy ponownie paczki Abstractions ze swoich źródeł.

## Dowód

Autorytatywne artefakty etapu znajdują się w repo SDK:

- `docs/m15-1-storage-sdk-inventory.md`;
- `inventory/storage-runtime-0.1.json`;
- `docs/m15-2-storage-sdk-ownership.md`;
- `scripts/verify-m15-2.ps1`.

Bramka chroni sumy źródeł, brak duplikatu i source reference w Runtime,
pakowanie SDK, 14 testów Runtime oraz pełne conformance providerów. Testy live
PostgreSQL wymagają osobnego sekretu środowiskowego.

## Granica

Etap nie oznacza publicznego wydania, podpisu, stabilności 1.0 ani gotowego SDK
dla innych języków hosta. Nie wydziela również jeszcze relational authoring i
nie tworzy template autora providera.
