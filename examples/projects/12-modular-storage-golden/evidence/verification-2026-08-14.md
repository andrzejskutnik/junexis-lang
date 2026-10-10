# Dowód M11.10 — 2026-08-14

## Wynik

`PASS` dla Golden Modular Storage E2E na Windows x64.

```json
{
  "result": "PASS",
  "provider": "sqlite",
  "restored_after_restart": true,
  "restored_observation": "living-room-0001",
  "rollback_clean": true,
  "build_id": "m11.10-golden-build",
  "bundle_id": "m11.10-golden-bundle",
  "modules": [
    "application::home_service",
    "dashboard::home_panel",
    "domain::observations",
    "main",
    "storage::home_store"
  ],
  "migrations": ["001-storage.sql"],
  "storage_contract": "junexis.storage/0.1",
  "panel_boundary": "PublishHomeSnapshot -> HomeStoragePanel"
}
```

## Wykonane kontrole

- `junexis check` — sukces,
- `junexis build` — sukces dla `server` i `report`,
- `go test ./...` wygenerowanego serwera — sukces dla pięciu pakietów,
- natywny build Windows x64 — sukces,
- zapis przez REST — sukces,
- rollback transakcji — brak pozostawionego rekordu,
- restart całego procesu — sukces,
- odczyt zatwierdzonego rekordu przez REST nowego procesu — sukces,
- moduły i migracja obecne w wygenerowanych manifestach.

Identyfikatory w powyższym JSON są kontrolowanymi identyfikatorami próby, nie
potwierdzeniem opublikowanego wydania. Końcowa materializacja System Bundle
dotarła do budowy aplikacji raportowej, lecz `esbuild` nie otrzymał w tej sesji
uprawnienia odczytu katalogu nadrzędnego. Dlatego pakiet nie jest oznaczony jako
zweryfikowany.

## Domknięcie

Ograniczenie zostało usunięte 2026-08-21. Aktualny dowód utworzenia,
weryfikacji i uruchomienia paczki znajduje się w
[`system-bundle-verification-2026-08-21.md`](system-bundle-verification-2026-08-21.md).
