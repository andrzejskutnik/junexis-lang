# Importowane named query

Projekt rozdziela model danych od zachowania aplikacji:

```text
src/data/home.jnx
    storage HomeData + RecentObservations
             │ selective import
             ▼
src/main.jnx
    task ReadHome @server
```

Developer importuje logiczny model `HomeData`, a następnie wywołuje jego
typowane query tak samo jak deklarację lokalną. Moduł aplikacji nie zna SQL,
drivera ani wybranego providera.

```powershell
junexis check
junexis build
```

Reprodukowalny dowód wykonawczy uruchamia `./scripts/verify-m13-11.ps1` z
katalogu repozytorium.
