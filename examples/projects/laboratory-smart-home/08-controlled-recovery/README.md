# Laboratorium Junexis — Dom 8: kontrolowany powrót

Ten przyrost odróżnia wznowienie źródła od odzyskania sterowania. Salon musi
dostarczyć dwa kolejne świeże pomiary. W trybie ręcznym potrzebna jest także
decyzja człowieka, a po niej jeszcze jeden aktualny pomiar.

## Uruchomienie

Z katalogu `junexis-lang`:

```powershell
junexis check examples/projects/laboratory-smart-home/08-controlled-recovery
junexis run examples/projects/laboratory-smart-home/08-controlled-recovery --duration 90
```

Panel prowadzi najpierw przez recovery automatyczne, a następnie przez recovery
zatwierdzane. W obu wariantach stan wentylacji salonu jest zachowany do chwili
uzyskania wymaganego dowodu.

## Automatyczna kontrola

```powershell
& examples/projects/laboratory-smart-home/08-controlled-recovery/verification/verify-controlled-recovery.ps1
```

