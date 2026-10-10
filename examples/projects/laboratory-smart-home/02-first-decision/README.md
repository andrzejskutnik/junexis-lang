# Dom 2 — pomiar staje się decyzją

Drugi przyrost tego samego systemu. Czujnik salonu publikuje naprzemiennie
`420 ppm` i `1250 ppm`. `AirQualityDecision` porównuje każdy pomiar z progiem
`1000 ppm` i publikuje jawną strukturę `HomeDecision`.

```powershell
junexis doctor
junexis check
junexis artifact-gate --target server
junexis run --duration 10
```

Oczekiwane wyniki:

```text
pomiar=420 ppm ... wynik=observe
pomiar=1250 ppm ... wynik=enable-ventilation
```

`enable-ventilation` jest na tym etapie decyzją, nie potwierdzeniem wykonania.
Projekt nie zawiera jeszcze sterownika wentylacji, panelu ani scenariusza
awarii.
