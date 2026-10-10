# 01 — Temperature Monitor project

Status: **project-first example / NEAR-RUNNABLE**

Ten katalog jest projektową wersją płaskiego przykładu
`examples/01-temperature-monitor.jnx`. Płaski plik pozostaje corpusowym testem
regresyjnym, a ten katalog jest wzorcem docelowego workflow Junexis:

```text
01-temperature-monitor/
  junexis.project.toml
  src/
    main.jnx
  user/
    embedded/
      platformio.ini
  out/        # generowane lokalnie
  .junexis/   # cache/diagnostyki generowane lokalnie
```

Podstawowa praca z projektem:

```powershell
dotnet run --project ../../../compiler/JunexisLang.Compiler.CLI -- check
dotnet run --project ../../../compiler/JunexisLang.Compiler.CLI -- build
```

`out/` i `.junexis/` nie są commitowane. Artefakty są lokalne dla projektu.

`user/embedded/platformio.ini` jest świadomym wyjątkiem od edycji `out/`.
Developer może zmieniać biblioteki, flagi kompilatora, board options albo upload
settings w pliku wersjonowanym w Git. Podczas `build` Junexis kopiuje ten plik
do `out/embedded/platformio.ini`, dzięki czemu PlatformIO nadal pracuje na
normalnym projekcie w `out/`, ale źródłem prawdy pozostaje `user/`.

Ten sam mechanizm overlayów obejmuje też:

```text
user/server/go.mod           -> out/server/go.mod
user/embedded/CMakeLists.txt -> out/embedded/CMakeLists.txt
```

Dodawaj te pliki dopiero wtedy, gdy projekt naprawdę potrzebuje przejąć kontrolę
nad konfiguracją Go albo CMake.
