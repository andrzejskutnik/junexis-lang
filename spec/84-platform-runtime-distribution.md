# M16.6 — dystrybucja Runtime

## Kontrakt zestawu

Komponent `runtime` lokalnego wydania platformy ma własny artefakt ZIP i
kontrakt `junexis.runtime.release/0.1`. Wersja komponentu i manifestu
wewnętrznego musi być identyczna z wersją całego wydania.

Zestaw zawiera trzy jawne warstwy:

| Warstwa | Kontrakt | Postać |
|---|---|---|
| `.NET` | `junexis.runtime.dotnet/0.1` | dwie zgodne paczki NuGet |
| embedded C | `junexis.runtime.embedded-c/0.1` | źródła, nagłówki i CMake |
| embedded Rust | `junexis.runtime.embedded-rust/0.1` | crate w stanie `source-only` |

Warstwa .NET składa się z `JunexisLang.Runtime` i jego jawnej zależności
`JunexisLang.Compiler.Core`. Obie paczki muszą mieć wersję wydania. Manifest
zawiera zamknięty inwentarz plików z rozmiarem i SHA-256.

## Znaczenie `available`

`available` oznacza, że powyższy zestaw istnieje i każda warstwa przechodzi
własny test konsumencki. Nie oznacza, że:

- artefakt jest samodzielnym programem;
- generatory Go, TypeScript i C++ korzystają już z tych samych bibliotek;
- Runtime został zainstalowany w lokalnym źródle paczek;
- publiczny rejestr lub adres pobierania istnieje.

Z tego powodu komponent ma `selfContained=false`, mimo że jest `verified`.

## Wymagany dowód

Kontrola wydania musi obejmować spójność manifestu i inwentarza, tożsamość
NuGet, wykonanie czystego konsumenta .NET, kompilację oraz test C i regresję
biblioteki .NET Runtime. Crate Rust pozostaje objęty inwentarzem, lecz ma stan
`source-only`, dopóki środowisko wydawnicze nie dostarczy dowodu kompilacji.
Samo utworzenie ZIP nie wystarcza.
