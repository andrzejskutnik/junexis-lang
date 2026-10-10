# Local Deployment Runtime Resume Contract

## Cel

Ten dokument definiuje minimalny kontrakt potrzebny do **prawdziwego runtime
resume** dla `junexis deploy local`. Nie opisuje obecnego stanu `M7.6` jako
gotowego resume. Obecny `deployment-receipt.json` jest kontraktem **wyniku
sesji bounded local deployment**, a nie trwałym uchwytem do wznowienia żywego
runtime.

## Stan obecny

Aktualny flow potrafi:

- uruchomić bounded local session dla `server` i `report`,
- zapisać deterministyczny `deployment-receipt.json` po zakończeniu sesji,
- zapisać deterministyczny `deployment-session.json` podczas życia i zakończenia sesji,
- odczytać i ocenić ten snapshot przez `junexis deploy local status --session <path>`,
  wraz z aktywnym probe targetów, jeśli sesja niesie endpoint/pid evidence,
- bezpiecznie zamknąć orphaned albo expired-owner session przez `junexis deploy local stop --session <path>`,
  albo aktywną sesję po jawnej weryfikacji pasującej owner identity,
- jawnie przepisać ownership przez `junexis deploy local adopt --session <path>`
  na nazwaną zewnętrzną tożsamość hosta, gdy poprzedni owner lease nie jest już aktywny,
- odtworzyć w Studio ostatni snapshot lifecycle i per-target status/details,
- przywrócić prepared deploy state, jeśli nadal istnieją bundle i preflight,
  oraz utrzymać lease dla `Live resumable session`,
  aby ponownie uruchomić bounded `Run Local` bez kolejnego `Prepare`.

Aktualny flow **nie potrafi**:

- wznowić już działającego procesu po restarcie Studio,
- utrzymać lease nowego ownera bez działającego hosta odpowiedzialnego za renewal,
- przejąć aktywnego ownera (`adopt`) dopóki jego lease jest nadal ważny,
- odróżnić z pełną pewnością wszystkich przypadków live runtime bez przyszłego
  aktywnego probe/heartbeat contract ponad sam snapshot.

## Problem

`deployment-receipt.json` jest zapisywany w `finally`, czyli po zakończeniu albo
przerwaniu bounded session. Z tego powodu receipt nie może być traktowany jako
źródło prawdy dla aktywnego runtime resume. Do prawdziwego resume potrzebny jest
oddzielny, aktualizowany w trakcie życia sesji kontrakt z jawnie zarządzaną
tożsamością procesu i lease ownership.

## Wymagany model

Prawdziwy runtime resume wymaga dwóch osobnych artefaktów:

1. `deployment-session.json`
2. `deployment-receipt.json`

`deployment-session.json` opisuje **żywą lub niedawno zakończoną sesję**.
`deployment-receipt.json` pozostaje kontraktem **wyniku końcowego**.

## Proponowany `deployment-session.json`

Minimalne pola:

```json
{
  "schemaVersion": "junexis.deployment-session/0.2",
  "sessionId": "01K1...",
  "buildId": "<sha256>",
  "bundleId": "<sha256>",
  "state": "starting|running|stopping|stopped|lost",
  "owner": {
    "tool": "junexis-studio|junexis-cli",
    "processId": 1234,
    "instanceId": "<stable-host-instance-id>",
    "leaseExpiresAtUtc": "2026-07-31T10:00:00.0000000Z"
  },
  "prepared": {
    "bundlePath": ".../system-bundle.zip",
    "packageReceiptPath": ".../system-bundle.zip.package-receipt.json",
    "preflightReportPath": ".../deployment-preflight.json"
  },
  "targets": [
    {
      "target": "server",
      "state": "starting|running|stopped|lost",
      "processId": 1234,
      "processStartedAtUtc": "2026-07-31T09:58:00.0000000Z",
      "processExecutablePath": ".../junexis-server",
      "endpoints": {
        "http": "http://127.0.0.1:7100",
        "health": "http://127.0.0.1:7101/healthz"
      },
      "lastHeartbeatUtc": "2026-07-31T09:59:10.0000000Z"
    }
  ]
}
```

## Semantyka

- `sessionId` musi być nową tożsamością każdej bounded local session.
- `state=running` wolno publikować dopiero po potwierdzonym readiness dla
  wszystkich wymaganych targetów.
- `leaseExpiresAtUtc` chroni przed przejęciem kontroli nad obcym procesem bez
  jawnego wygaszenia lease.
- `lastHeartbeatUtc` musi być odświeżane przez właściciela sesji tak długo, jak
  runtime pozostaje żywy.
- `state=lost` oznacza, że istnieje artefakt sesji, ale nie ma już wiarygodnego
  dowodu, że właściciel nadal kontroluje proces.
- `stop --session` może działać wtedy, gdy owner lease nie jest już aktywny,
  albo gdy caller jawnie poda pasujące `owner-tool` i `owner-instance-id`.
- proces targetu może zostać zatrzymany wyłącznie po zgodności PID, czasu startu
  i kanonicznej ścieżki executable; chroni to przed ponownym użyciem PID.
- `healthUrl` musi używać HTTP, adresu loopback i ścieżki `/health/ready`.
- `adopt --session` może przepisać ownership tylko na jawnie podaną tożsamość
  hosta i tylko wtedy, gdy poprzedni owner lease nie jest już aktywny.

## Minimalne API CLI

Do prawdziwego resume potrzebne są nowe komendy lub opcje. Aktualny baseline
ma już snapshot status, owner-aware stop i jawne adopt, ale wciąż bez pełnego
ciągłego ownership handoff po adopcji:

- `junexis deploy local --session-out <path>`
- `junexis deploy local status --session <path>`
- `junexis deploy local stop --session <path>`
- `junexis deploy local adopt --session <path> --owner-tool <tool> --owner-instance-id <id> --owner-process-id <pid>`

Semantyka:

- obecne `status` ocenia lease ownera i aktywnie sprawdza targety, gdy sesja
  niesie probeable endpoint albo `pid`.
- obecne `stop` jest dozwolone wtedy, gdy owner lease nie jest już aktywny,
  albo gdy caller poda pasującą owner identity dla aktywnej sesji.
- obecne `adopt` jest jawne i auditowalne, zapisuje PID długotrwałego hosta,
  a ponowne wywołanie przez tego samego właściciela odnawia lease. Dalsze
  odnawianie zapewnia host.
- Studio może odnowić własny, już posiadany lease przy `Refresh` i w tle,
  ale nie może po cichu adoptować obcego ownera.

## Wymagania dla Studio

Studio po restarcie powinno rozróżniać trzy stany:

1. `Historical snapshot only`
2. `Rerunnable prepared state`
3. `Live resumable session`

Aktualne `M7.6` rozróżnia wszystkie trzy stany. `Live resumable session` oznacza
potwierdzony live runtime i odnawialne ownership, ale nie odtwarza procesu, który
faktycznie zakończył działanie.

## Niezbędne zabezpieczenia

- PID bez lease i heartbeat nie jest dowodem posiadania sesji.
- Lokalne endpointy muszą być weryfikowane aktywnie przez `status`, nie tylko
  odczytywane z pliku.
- `adopt` musi odrzucać obce lub nieaktualne `buildId`/`bundleId`.
- `stop` nie może zabijać procesu, którego ownership nie jest potwierdzone.

## Granica zamknięcia M7.6

M7.6 obejmuje lifecycle receipt/session, aktywne evidence żyjących targetów,
jawny handoff wygasłego lub osieroconego ownera, renewal przez ten sam host oraz
owner-aware stop. Realny smoke między repozytoriami potwierdza ten przepływ po
przerwaniu procesu właściciela CLI.

M7.6 nie obiecuje rekonstrukcji procesu, który zakończył działanie. Taki restart
wymaga przyszłego supervisora, polityki restartu, ponownego wiązania endpointów
i osobnego dowodu zgodności z Build ID. Nie należy nazywać tej przyszłej funkcji
częścią obecnego kontraktu `Live resumable session`.
