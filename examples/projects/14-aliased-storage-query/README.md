# Lokalny alias modelu storage

Moduł aplikacji importuje `HomeData` pod krótszą nazwą `Store`:

```junexis
import data::home { HomeData as Store }

task ReadHome(homeId: Uuid) @server
    -> List<Store_RecentObservationsRow> {
    return Store_RecentObservations(homeId: homeId)
}
```

`Store` istnieje wyłącznie w kodzie tego modułu. Kompilator przed utworzeniem
wspólnego IR obniża lokalne nazwy do `HomeData_RecentObservations` i
`HomeData_RecentObservationsRow`. Runtime nadal otrzymuje model `HomeData` z
niezmienionym fingerprintem.

Powtarzalny dowód wykonawczy uruchamia `./scripts/verify-m13-12.ps1` z katalogu
repozytorium.
