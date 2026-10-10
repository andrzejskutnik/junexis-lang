# Dom 1 — pierwszy czujnik

Pierwszy przyrost systemu „Dom, który zauważa i reaguje”. Symulator salonu co
dwie sekundy tworzy przewidywalny pomiar jakości powietrza `420 ppm` i publikuje
go jako `SensorReading`. Techniczny podgląd odbiera wartość i pokazuje ją w
dzienniku, ale nie podejmuje żadnej decyzji.

Projekt celowo nie zawiera jeszcze reguły, wentylacji, panelu ani awarii. Jego
jedyną odpowiedzialnością jest utworzenie poprawnej informacji domenowej.

```powershell
junexis doctor
junexis check
junexis build --target server
junexis artifact-gate --target server
junexis run --duration 8
```

`run` ponownie buduje projekt i uruchamia wygenerowany serwer w ograniczonej
sesji. Oczekiwanym dowodem są kolejne wpisy
`POMIAR pokoj=salon jakosc_powietrza=420 ppm`.
