# Dowód System Bundle M11 — 2026-08-21

## Wynik

`PASS` dla integralności paczki, serwera Windows x64, aplikacji raportowej i
odtworzenia SQLite po restarcie procesu uruchomionego wyłącznie z paczki.

```json
{
  "result": "PASS",
  "schema": "junexis.system-bundle/0.1",
  "build_id": "345445370763cd03cf81fa9cc976fdc79c393f02c3b0352656f208d98f6b32fd",
  "bundle_id": "4bb3b2db62dccd9671ae187385ebc3badb98c62eac09a5779c3308da4fe5a63f",
  "artifact_entries": 37,
  "verified_payload_files": 39,
  "targets": ["report", "server"],
  "modules": [
    "application::home_service",
    "dashboard::home_panel",
    "domain::observations",
    "main",
    "storage::home_store"
  ],
  "provider": "sqlite",
  "restored_after_restart": true,
  "restored_observation": "bundle-living-room-0001",
  "report_ready": true,
  "report_bundle_bytes": 45702,
  "checkout_required_at_runtime": false,
  "go_toolchain_required_at_runtime": false,
  "c_toolchain_required_at_runtime": false
}
```

## Zakres próby

- `junexis package` przeszedł analizę, natywne bramki, bound build,
  materializację runtime oraz ponowną weryfikację System Bundle,
- `junexis bundle verify` potwierdził 39 plików manifestu integralności,
- paczka została rozpakowana do izolowanego katalogu tymczasowego,
- uruchomiono tylko `artifacts/server/runtime/junexis-server.exe` oraz
  `artifacts/report/runtime/serve.mjs`,
- panel podał stronę wejściową i samodzielny bundle przeglądarkowy,
- obserwację zapisano przez REST, po czym cały proces serwera zrestartowano,
- drugi proces odczytał rekord z SQLite i zwrócił tożsamość tej samej paczki.

## Autentyczność

Paczka jest integralna, ale niepodpisana. Weryfikator zgłosił `UNSIGNED`, a
jawna polityka permissive dopuściła uruchomienie. To wystarcza do technicznego
dowodu M11, lecz nie zastępuje podpisu przyszłego publicznego wydania.
