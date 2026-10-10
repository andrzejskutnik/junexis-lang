# Laboratorium Junexis — Dom 9: polityka recovery

Dom 9 oddaje użytkownikowi odpowiedzialność za kontrakt powrotu salonu. Panel
pokazuje pełną efektywną konfigurację, pozwala porównać recovery automatyczne i
ręczne, odrzucić powrót, ponowić walidację oraz wykonać rollback.

```powershell
junexis check examples/projects/laboratory-smart-home/09-recovery-policy
junexis run examples/projects/laboratory-smart-home/09-recovery-policy --duration 120
```

Automatyczny odbiór:

```powershell
& examples/projects/laboratory-smart-home/09-recovery-policy/verification/verify-recovery-policy.ps1
```

