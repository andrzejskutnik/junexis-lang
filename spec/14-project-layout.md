# Project layout and artifact isolation (0.2 Experimental)

Status: **0.2 Experimental — design direction**
Data: **2026-07-01**

Ten dokument opisuje docelowy model katalogu projektu Junexis. Nie zmienia
jeszcze składni języka ani nie wymaga natychmiastowej migracji wszystkich
istniejących przykładów.

## Problem

W prototypie kompilator pracował przede wszystkim na pojedynczym pliku `.jnx`.
Domyślne katalogi wyjściowe typu `out/` były wygodne dla szybkich testów, ale
przy wielu przykładach i wielu targetach zaczynają mieszać artefakty:

```text
examples/
  01-temperature-monitor.jnx
  02-ecommerce-api.jnx
  out/
    server/
    embedded/
    report/
```

Taki układ utrudnia:

- odróżnienie artefaktów różnych systemów,
- czyszczenie wygenerowanych plików,
- uruchamianie `artifact-gate` per projekt,
- przechowywanie cache, diagnostyk i wyników builda,
- przyszłą obsługę wielu plików, importów i IDE workspace.

Junexis opisuje system rozproszony, więc jednostką pracy powinna być docelowo
nie tylko pojedyncza ścieżka `.jnx`, ale **projekt systemu**.

## Docelowy layout projektu

Docelowy minimalny projekt powinien wyglądać tak:

```text
01-temperature-monitor/
  junexis.project.toml
  src/
    main.jnx
  migrations/
    001-initial.sql
  out/
    server/
    embedded/
    report/
    deployment/
  .junexis/
    cache/
    build/
    diagnostics/
    state/
```

Znaczenie katalogów:

| Ścieżka | Rola |
|---------|------|
| `junexis.project.toml` | Manifest projektu: nazwa, entrypoint, targety, katalogi i przyszłe profile. |
| `src/main.jnx` | Główne wejście języka dla projektu. |
| `migrations/*.sql` | Wersjonowane, niezmienne migracje providera SQLite dla `@server`. |
| `out/server/` | Artefakty generowane dla backendu/server runtime. |
| `out/embedded/` | Artefakty generowane dla targetów embedded. |
| `out/report/` | Artefakty generowane dla report/UI/browser/WASM. |
| `out/deployment/` | Artefakty deploymentu, np. docker-compose, manifesty i wiring. |
| `out/laboratory/` | Zweryfikowane System Bundle i samodzielne paczki `.junexis-lab.zip`. |
| `out/debug/junexis.source-map.json` | Wersjonowana mapa deklaracji `.jnx` do zakresów funkcji wygenerowanych backendów. |
| `.junexis/cache/` | Cache toolchaina Junexis, IR, indeksy i dane pomocnicze. |
| `.junexis/build/` | Tymczasowe katalogi builda i artifact-gate. |
| `.junexis/diagnostics/` | Opcjonalne snapshoty diagnostyk, raporty i logi narzędziowe. |
| `.junexis/state/` | Trwałe, wersjonowane punkty synchronizacji narzędzi, w tym `latest-build.json`. |

`out/` jest przeznaczone na wynik, który człowiek może obejrzeć albo spakować.
`.junexis/` jest roboczym stanem narzędzi i domyślnie nie powinno być
commitowane.

## Minimalny manifest

Początkowy manifest powinien być mały i jawny:

```toml
name = "temperature-monitor"
entry = "src/main.jnx"
sourceRoots = ["src"]
localLibraries = ["home_db=../home-db/src"]
packages = ["junexis.observation@0.1.0-experimental"]
packageIndex = "registry/index.json"
targets = ["server"]
outDir = "out"
```

Pola:

| Pole | Znaczenie |
|------|-----------|
| `name` | Stabilna nazwa projektu i prefiks artefaktów. |
| `entry` | Główny plik `.jnx` względem katalogu projektu. |
| `sourceRoots` | Katalogi modułów `.jnx` względem projektu; domyślnie `["src"]` dla zgodności ze starszym manifestem. |
| `localLibraries` | Opcjonalne lokalne biblioteki jako `namespace=ścieżka`; ścieżka jest względna wobec projektu. |
| `packages` | Opcjonalne pakiety aplikacyjne jako `id@dokładna-wersja`; kontrakt 0.1 nie rozwiązuje zakresów wersji. |
| `packageIndex` | Lokalny statyczny indeks `junexis.package-index/0.1`, względny wobec projektu i wymagany przez `packages`. |
| `providerPackages` | Opcjonalne lokalne pakiety providerów wewnątrz projektu. |
| `targets` | Domyślne targety dla `junexis build` i `artifact-gate`. |
| `outDir` | Katalog wynikowy względem projektu. Domyślnie `out`. |

Szablon `junexis init` tworzy minimalne zadanie `@server`, dlatego wybiera tylko
`server`. Kolejne targety dodaje się razem z odpowiadającymi im zadaniami.
`doctor` i `build` odrzucają target zadeklarowany bez pasującego zadania zamiast
publikować pozornie kompletny zestaw artefaktów.

`sourceRoots` jest aktywnym kontraktem narzędzi. CLI kataloguje wszystkie pliki
`.jnx` pod tymi katalogami, wyprowadza tożsamość modułu ze ścieżki względnej i
odrzuca entrypoint poza katalogami źródeł. Przykładowo
`src/domain/observations.jnx` ma tożsamość `domain::observations`. Katalogi nie
mogą wychodzić poza projekt, nakładać się ani wytwarzać tożsamości różniących
się wyłącznie wielkością liter.

Lokalna biblioteka otrzymuje własny prefiks przestrzeni. Wpis
`home_db=../home-db/src` sprawia, że `worker.jnx` jest importowany jako
`home_db::worker`. Udany build zapisuje `out/debug/junexis.modules.lock.json`
z uporządkowaną listą tożsamości, ścieżek i SHA-256 wszystkich źródeł. Ten plik
jest dowodem dokładnego lokalnego grafu, nie obietnicą zdalnego rejestru.

`junexis restore` odtwarza pakiety ze statycznego indeksu do
`.junexis/cache/packages/<id>/<wersja>/`, sprawdza SHA-256 ZIP-a, manifestu i
zadeklarowanych plików oraz zapisuje `junexis.packages.lock`. Eksporty pakietu
stają się zwykłymi katalogami wejściowymi `ProjectModuleCatalog`, więc importy
nie mają osobnej semantyki. `restore --offline` wymaga zgodnego lockfile i
kompletnego cache; nie czyta indeksu. Pierwszy kontrakt nie obsługuje zdalnego
Registry, podpisów ani zakresów wersji zależności.

M12.2 dodaje jawne bindingi storage:

```toml
providerPackages = ["providers/postgresql"]

[storage.home]
declaration = "HomeStore"
provider = "postgresql"
providerVersion = "0.1.0"
config.connection = "secret:HOME_DATABASE_URL"
```

Pakiet musi pozostać wewnątrz projektu i zawierać
`junexis.provider.toml`. Build sprawdza dokładną wersję, schemat konfiguracji,
pliki pakietu i powiązanie z deklaracją storage, a następnie zapisuje
`out/debug/junexis.providers.lock.json`. Wartości sekretów nie trafiają do
blokady. Dokładny kontrakt opisuje
[`32-provider-package-and-binding.md`](32-provider-package-and-binding.md).

M16.10 dodaje drugi, produkcyjnie bliższy wariant źródła paczki. Zamiast
kopiować provider do projektu, manifest wskazuje dokładną wersję SDK:

```toml
providerSdkVersion = "0.1.0-preview.10"

[storage.home]
declaration = "HomeStore"
provider = "postgresql"
providerVersion = "0.1.0"
config.connection = "secret:HOME_DATABASE_URL"
```

`doctor` i `build` rozwiązują wtedy provider z rejestru profilu użytkownika
M16.9. `providerPackages` pozostaje trybem lokalnym; obu mechanizmów nie wolno
mieszać w jednym manifeście. Szczegóły opisuje
[`88-platform-project-provider-resolution.md`](88-platform-project-provider-resolution.md).

Jeśli istnieje katalog `migrations/`, build kopiuje jego pliki `.sql` do
`out/server/migrations/` w porządku nazw. Pliki pozostają własnością autora;
generator ich nie zmienia. Referencyjny provider zapisuje SHA-256 zastosowanej
migracji i odrzuca późniejszą zmianę jej treści.

Rozszerzenia manifestu powinny pojawiać się dopiero wtedy, gdy implementacja
ich potrzebuje. Kandydaci na później:

```toml
[profiles]

[devices]

[deployment]

[runtime]

[artifactGate]

[toolchains]
```

## Tryby pracy CLI

CLI powinno ewolucyjnie obsłużyć dwa tryby:

> **Decyzja D18:** workflow Junexis jest project-first. Tryb pojedynczego pliku
> pozostaje potrzebny, ale jako compatibility/playground/regression corpus, a
> nie równorzędny kierunek organizacji systemów.

### Tryb pojedynczego pliku

Ten tryb pozostaje potrzebny dla korpusów gramatyki, testów i szybkich
eksperymentów:

```text
junexis check examples/01-temperature-monitor.jnx
junexis build examples/01-temperature-monitor.jnx --target server
```

Domyślny output dla pojedynczego pliku jest izolowany per nazwa pliku,
a nie współdzielony dla całego katalogu:

```text
examples/.junexis/out/01-temperature-monitor/server/
```

albo jawnie:

```text
junexis build examples/01-temperature-monitor.jnx --out examples/out/01-temperature-monitor
```

### Tryb projektu

Jeżeli wejściem jest katalog lub bieżący katalog zawiera `junexis.project.toml`,
CLI powinno traktować go jako projekt:

```text
junexis check
junexis build
junexis artifact-gate
junexis clean
junexis doctor
```

Wtedy:

- entrypoint pochodzi z manifestu,
- targety domyślne pochodzą z manifestu,
- każdy target manifestu musi odpowiadać co najmniej jednemu zadaniu w źródle,
- `out/` jest lokalne dla projektu,
- `.junexis/` przechowuje cache i roboczy stan builda,
- udany build publikuje atomowo `.junexis/state/latest-build.json`, który
  identyfikuje dokładny `buildId` i bezwzględne ścieżki kontraktów w `out/`,
- `clean` usuwa `out/`, `.junexis/build/`, `.junexis/diagnostics/` i
  `.junexis/state/`, ale nie
  rusza źródeł, manifestu ani `.junexis/cache/`.
- `doctor` raportuje, który projekt/plik jest aktywny, czy manifest, entry i
  zgodność targetów ze źródłem są poprawne oraz czy narzędzia nie wracają
  nieświadomie do file-mode.

## Plan migracji

Migracja powinna być ewolucyjna:

1. Udokumentować layout projektu i manifest.
2. Dodać `junexis init`, które tworzy `junexis.project.toml`, `src/main.jnx`,
   `out/` i `.junexis/`.
3. Dodać parser manifestu do CLI bez zmiany zachowania pojedynczych plików.
4. Dodać project discovery: katalog wejściowy lub bieżący katalog z manifestem.
5. Zmienić domyślny output dla projektów na `out/<target>/`.
6. Dodać izolowany output dla trybu pojedynczego pliku.
7. Zmigrować pierwszy przykład jako projekt, np.
   `examples/projects/01-temperature-monitor/`.
8. Dopiero po stabilizacji migrować kolejne przykłady.

Obecne pliki `examples/*.jnx` mogą pozostać jako płaski korpus regresyjny.
Nie trzeba ich natychmiast usuwać ani przenosić.

## Relacja do języka

Manifest projektu nie zastępuje konstrukcji języka:

| Język | Manifest projektu |
|-------|-------------------|
| `task` opisuje zachowanie | `entry` wskazuje plik wejściowy |
| `device` opisuje sprzęt | `toolchains` może wybrać lokalny toolchain |
| `pipeline` opisuje przepływ | `outDir` izoluje artefakty przepływu |
| `deployment` opisuje instancje | `out/deployment/` przechowuje wygenerowane manifesty |
| `protocol` opisuje granice platform | manifest może wybrać profil builda |
| `narrative` opisuje obserwowalność | `.junexis/diagnostics/` może przechowywać snapshoty |

To rozróżnienie jest ważne: manifest jest narzędziowym kontraktem projektu,
a nie składnią systemu rozproszonego.

## Zasady implementacyjne

- Nie łamać istniejącego `junexis check <plik.jnx>` i `junexis build <plik.jnx>`.
- Nie mieszać artefaktów wielu projektów w jednym wspólnym `out/`.
- Nie commitować `.junexis/` ani lokalnych cache toolchainów.
- `out/` może być commitowane tylko świadomie, np. dla fixture albo snapshotu.
- `artifact-gate` powinien używać katalogu roboczego projektu lub krótkiej
  ścieżki tymczasowej, jeśli wymaga tego toolchain.
- VS Code extension powinno wykrywać najbliższy `junexis.project.toml` dla
  aktywnego pliku i uruchamiać komendy w kontekście projektu.

## Status implementacji

Na dzień 2026-07-01:

- `junexis init` generuje minimalny layout projektu i manifest
  `junexis.project.toml`,
- parser manifestu i project discovery są dostępne w CLI,
- `check`, `build`, `deploy` i `artifact-gate` obsługują opcjonalny tryb
  projektu bez łamania istniejącego trybu pojedynczego pliku,
- dla projektu `build` używa manifestowego `entry`, `targets` i `outDir`,
- dla projektu narzędzia odkrywają ostatni ukończony build przez
  `.junexis/state/latest-build.json`,
- pierwszy przykład project-first istnieje w
  `examples/projects/01-temperature-monitor/`,
- tryb pojedynczego pliku bez `--out` generuje do
  `<katalog-pliku>/.junexis/out/<nazwa-pliku>/`,
- `junexis clean` obsługuje projektowy layout bez usuwania źródeł ani cache,
- `junexis doctor` raportuje project/file-mode context i podstawowe problemy
  layoutu,
- tryb pojedynczego pliku pozostaje trybem compatibility/playground, nie
  docelowym wzorcem organizacji systemów,
- przykłady są nadal płaskim korpusem regresyjnym,
- `scripts/verify-junexis-02.ps1` zapewnia powtarzalną bramkę 0.2,
- `scripts/verify-junexis-02.ps1` obejmuje project-first workflow dla
  `examples/projects/01-temperature-monitor`,
- embedded `artifact-gate` wymaga na Windows krótkich ścieżek dla PlatformIO.
