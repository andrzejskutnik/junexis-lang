# Dom 3 — wykonaj decyzję

Trzeci przyrost inteligentnego domu rozdziela decyzję, polecenie i wykonanie.
`VentilationController` zamienia `HomeDecision` w `VentilationCommand`, a
`VentilationActuator` aktualizuje symulowany stan i publikuje
`VentilationConfirmation`.

```powershell
junexis doctor
junexis check
junexis artifact-gate --target server
junexis run --duration 10
```

Oczekiwane potwierdzenia:

```text
zadano=false stan=false status=applied
zadano=true stan=true status=applied
```

Wykonawca jest nadal lokalną symulacją `@server`. Projekt nie udaje fizycznej
wentylacji, nie zawiera panelu i nie wprowadza awarii ani odmowy wykonania.
