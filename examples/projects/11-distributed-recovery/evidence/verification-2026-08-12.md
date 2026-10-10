# Weryfikacja recovery rozproszonego — 2026-08-12

Sprawdzony zakres:

- `check`, analiza i generowanie dla `embedded`, `server` oraz `report`,
- natywna bramka firmware ESP32-C3 przez PlatformIO,
- natywna bramka serwera Go,
- kompilacja raportu TypeScript,
- transport urządzenie → serwer zgodny z wygenerowaną ramką TCP/JSON,
- transport serwer → raport przez WebSocket i MessagePack,
- trwały zapis `last_boot_id` oraz `recovery_generation` po każdym odebranym
  komunikacie,
- prawdziwe zatrzymanie i ponowne uruchomienie procesu serwera,
- automatyczny reconnect nadal działającego procesu raportu.

Wynik testu wykonawczego:

```json
{
  "result": "PASS",
  "embedded_contract": "tcp-json-framed",
  "server_report_contract": "websocket-messagepack",
  "first_boot": "boot-alpha",
  "first_generation": 1,
  "server_restarted": true,
  "second_boot": "boot-beta",
  "second_generation": 2,
  "report_reconnected": true
}
```

Pełny zestaw generatorów po naprawach: `249/249 PASS`.

Po przebudowie kursu sprawdzono również dwa eksperymenty użytkownika:

- ponowne wysłanie `boot-beta` zachowało generację `1`,
- sekwencja `boot-alpha → boot-beta → boot-gamma` utworzyła generacje
  `1 → 2 → 3`.

Oba warianty zakończyły się `PASS` i zachowały reconnect raportu po restarcie
serwera.

Test bez sprzętu nie zastępuje próby na ESP32-C3. Potwierdza dokładny kontrakt
wejściowy firmware i rzeczywiste wykonanie dalszych dwóch węzłów; radio,
zasilanie i fizyczny restart płytki pozostają osobnym odbiorem.
