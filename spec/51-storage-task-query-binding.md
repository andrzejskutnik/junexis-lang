# M13.10 — named query wywoływane z tasku

> **Status:** VERIFIED TASK-TO-STORAGE E2E
>
> **Target wykonawczy:** `@server` / Go
>
> **Zakres:** przenośne zapytania tylko do odczytu

## Idea

M13.9 wygenerował klienta targetu. M13.10 przenosi jego użycie na poziom kodu
Junexis: developer nie otwiera Runtime, nie buduje koperty JSON i nie wywołuje
funkcji Go.

```junexis
storage HomeData {
    entity Observation {
        id: Uuid
        homeId: Uuid
        sensor: Text
        value: Float
        key(id)
    }

    query RecentObservations(homeId: Uuid) from Observation {
        filter homeId == params.homeId
        project sensor as sensorName, value
        limit 20
    }
}

task ReadHome(homeId: Uuid) @server
    -> List<HomeData_RecentObservationsRow> {
    return HomeData_RecentObservations(homeId: homeId)
}
```

## Stabilna konwencja nazw

Każda operacja tworzy dwa symbole językowe:

| Deklaracja | Symbol |
|---|---|
| `storage HomeData` + `query RecentObservations` | `HomeData_RecentObservations(...)` |
| wynik query | `HomeData_RecentObservationsRow` |

Model kwalifikuje nazwę query. Podkreślenie jest świadomym separatorem i nie
wprowadza nowego słowa kluczowego ani dwuznaczności z dostępem do pola.

## Typowanie

Kompilator syntetyzuje strukturę wiersza z końcowego shape zapytania. Kolejne
`project` zmieniają pola oraz ich aliasy dokładnie tak jak Query Plan.
Wywołanie zwraca `List<...Row>`, dlatego zwykłe reguły `let`, `return`, `for`
i dostępu do pól widzą konkretny typ.

Argumenty mogą być pozycyjne w kolejności deklaracji albo nazwane w dowolnej
kolejności. Semantyka odrzuca niezgodną liczbę, obcą lub powtórzoną nazwę,
inny typ oraz użycie poza jednoznacznym taskiem `@server`. Argumenty nazwane są
porządkowane według deklaracji przed wygenerowaniem Go.

## Lifecycle wykonania

Generator sprawdza, czy task rzeczywiście wywołuje named query. Dopiero wtedy:

1. `main` otwiera jeden provider przez `OpenFromEnvironment`;
2. taski współdzielą bezpiecznego, sekwencyjnego klienta Runtime;
3. wrapper query wiąże argumenty i wywołuje kod M13.9;
4. wynik wire jest przekształcany do syntetycznego typu tasku;
5. provider jest zamykany przy końcu procesu.

Samo zadeklarowanie query nie wymusza otwarcia Hosta. Dzięki temu M13.9 nadal
pozwala aplikacji Go zarządzać bindingiem samodzielnie.

W bootstrapie 0.2 brak providera, błąd query lub niepoprawny wynik zatrzymuje
wykonanie fail-fast. Host przekazuje wyłącznie secret-safe przyczynę. Jawne
`Result`, retry i obsługa błędów w składni tasku wymagają osobnego kontraktu.

## Dowód Golden

```powershell
./scripts/verify-m13-10.ps1
```

Bramka kompiluje
[`24-storage-query-from-task.jnx`](../examples/grammar/24-storage-query-from-task.jnx),
uruchamia referencyjny Storage Runtime Host oraz provider memory, zapisuje
obserwację, a następnie wywołuje wygenerowaną funkcję tasku `ReadHome`.
Sprawdza dane wiersza oraz konwersję timestampu do `time.Time` po stronie
taskowego modelu Go.

## Granice

M13.10 nie dodaje:

- mutacji `insert/update/delete` w języku;
- wywołań storage w `fn`, `@embedded` lub `@report`;
- wywołań named query zadeklarowanych w importowanym module — granicę tę
  zrealizował M13.11;
- jawnej obsługi błędów query przez kod tasku;
- relacji, joinów, agregacji i migracji;
- współdzielenia jednego Hosta przez wiele bindingów w pojedynczym programie;
- automatycznego wyboru providera albo konfiguracji bazy.

Provider nadal jest decyzją deploymentu. Kod tasku zależy od logicznego modelu
i named query, nigdy od PostgreSQL, SQLite, SQL ani drivera.

Projektowy wariant z modelem w osobnym pliku opisuje
[`52-imported-storage-query-binding.md`](52-imported-storage-query-binding.md).
