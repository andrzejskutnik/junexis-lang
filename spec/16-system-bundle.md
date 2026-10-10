# Experimental System Bundle 0.1 — kontrakt M7.1

> **Status:** EXPERIMENTAL / VERIFIED dla M7.1
> **Identyfikator:** `junexis.system-bundle/0.1`
> **Zakres:** przenośny inventory ostatniego poprawnego buildu projektu

System Bundle jest artefaktem całego systemu, nie nowym plikiem wykonywalnym
Junexis. Łączy wygenerowane części dla poszczególnych platform ze wspólnymi
kontraktami builda. Wersja 0.1 nie deklaruje stabilności formatu, zgodności
wstecznej ani gotowości produkcyjnej.

## Granica M7.1

Polecenie:

```text
junexis bundle create [project] [--out bundle.zip]
```

korzysta wyłącznie z atomowego project-mode
`.junexis/state/latest-build.json`. Nie uruchamia kompilacji i nie zgaduje,
czy zmienione później źródło odpowiada buildowi. Każdy plik musi nadal istnieć
i mieć SHA-256 zapisane w `junexis.artifacts.json`; inaczej pakowanie zostaje
odrzucone przed utworzeniem ZIP.

Tryb pojedynczego pliku jest odrzucony. System Bundle jest świadomie
project-first.

## Struktura ZIP

```text
manifest.json
system-bundle.json
contracts/
└── junexis.artifacts.json
artifacts/
├── embedded/
├── server/
├── report/
├── deployment/
├── debug/
└── analysis/
```

Nieobecne platformy nie tworzą pustych katalogów. `system-bundle.json`
zawiera wyłącznie:

- schema `junexis.system-bundle/0.1`,
- nazwę projektu,
- Build ID,
- względną ścieżkę entry file,
- uporządkowane targety,
- uporządkowany inventory artefaktów z targetem, rodzajem, ownership i SHA-256.

Descriptor nie zawiera absolutnego project root ani output root. Artefakty
debug/analysis 0.2 są zachowywane bajtowo zgodnie z latest build i mogą nadal
zawierać absolutną tożsamość źródła. Relokowalny source identity jest osobnym
problemem do rozwiązania po M7.1; format 0.1 nie ukrywa tej granicy.

## Integralność i Bundle ID

`manifest.json` ma schema `junexis.system-bundle-integrity/0.1` i opisuje
SHA-256 dokładnie każdego innego wpisu ZIP. Bundle ID jest SHA-256 dokładnych
bajtów tego manifestu. Nie jest podpisem ani deklaracją zaufania.

Tworzenie jest deterministyczne dla tych samych bajtów latest build:

- wpisy są sortowane ordinalnie,
- timestamp ZIP jest stały,
- nie ma czasu utworzenia ani ścieżek hosta w descriptorze,
- dwa eksporty mają ten sam Bundle ID i bajty ZIP.

## Weryfikacja offline

```text
junexis bundle verify bundle.zip
```

sprawdza przed zaakceptowaniem:

1. limit 512 MiB archiwum i rozpakowanych danych,
2. limit 4096 wpisów i 64 MiB pojedynczego wpisu,
3. brak ścieżek absolutnych, traversal i duplikatów case-insensitive,
4. dokładną kompletność manifestu,
5. SHA-256 każdego payloadu,
6. schema i relacje descriptor ↔ artifact inventory ↔ rzeczywiste bajty,
7. zgodność Build ID,
8. brak dodatkowych, nieopisanych payloadów.

Weryfikacja nie rozpakowuje ZIP na dysk i nie uruchamia artefaktów.

## Bezpieczeństwo M7.1

Pakowanie odrzuca symlinki oraz oczywiste pliki sekretów: `.env`, nazwy
zawierające `secret` i klucze `.pem`, `.key`, `.pfx`, `.p12`. Nie zastępuje to
przyszłego deployment preflight ani pełnego skanowania sekretów.

System Bundle 0.1 nie ma podpisu. Autentyczność i polityka dopuszczenia są
planowane dopiero po ustabilizowaniu podstawowego inventory.
