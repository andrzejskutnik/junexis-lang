# M14.2 — składnia mutacji i projekcja źródła

> **Status:** VERIFIED COMPILER CONTRACT
>
> **Zakres:** gramatyka, AST, Structural IR, Mutation Plan i diagnostyki źródłowe
>
> **Wykonanie:** M14.3 dodaje lowering Runtime; od M14.4 memory, a od M14.5 SQLite wykonują zapis

## Cel

M14.1 ustalił bezpieczne znaczenie zapisu. M14.2 pozwala autorowi wyrazić tę
intencję bez SQL i bez ręcznego budowania IR:

```text
storage .jnx
    → parser i AST
    → bezstratny Structural IR
    → junexis.storage.mutation-plan/0.1
```

Kompilator wykonuje cały ten przepływ podczas analizy. Nie otwiera bazy i nie
uruchamia providera.

Projektor jest częścią wspólnego pipeline'u analiz strukturalnych, dlatego te
same diagnostyki trafiają do `junexis check`, pozostałych poleceń CLI oraz LSP.
Nie istnieje osobna, łagodniejsza walidacja tylko dla edytora.

## Trzy operacje

### Insert

```junexis
storage HomeData {
    entity Observation {
        id: Uuid
        homeId: Uuid
        sensor: Text
        value: Float
        recordedAt: Time
        note: Text | Null
        key(id)
    }

    mutation AddObservation(
        id: Uuid,
        homeId: Uuid,
        sensor: Text,
        value: Float,
        recordedAt: Time
    ) insert single Observation {
        set id = params.id
        set homeId = params.homeId
        set sensor = params.sensor
        set value = params.value
        set recordedAt = params.recordedAt
    }
}
```

`insert` zawsze wymaga `single`, nie ma `where` i przypisuje wszystkie pola
non-nullable. Pole nullable można pominąć.

### Update jednego rekordu

```junexis
mutation RenameSensor(id: Uuid, sensor: Text)
    update single Observation {
    set sensor = params.sensor
    where id == params.id
}
```

`single` oznacza zobowiązanie sprawdzane przez kompilator: `where` musi wiązać
każde pole klucza przez `==` z nie-nullable parametrem albo literałem. To nie
jest sugestia dla providera.

### Jawna operacja masowa

```junexis
mutation DeleteSensorHistory(sensor: Text)
    delete many Observation {
    where sensor == params.sensor
}
```

Słowo `many` jest wymaganym, widocznym wyborem autora. Samo pominięcie pełnego
klucza nie zmienia automatycznie operacji w masową.

## Reguły składni

```text
mutation <Nazwa>(<parametry>)
    <insert|update|delete> <single|many> <Encja> {
    set <pole> = <params.nazwa|literał>
    ...
    where <predykat>
}
```

- `set` występuje przed opcjonalnym `where`;
- `Insert` i `Update` wymagają przynajmniej jednego `set`;
- `Delete` nie przyjmuje `set`;
- `Update` i `Delete` wymagają dokładnie jednego `where`;
- `set` przyjmuje parametr albo literał, nie odczytuje bieżącego pola;
- `where` obsługuje ten sam przenośny podzbiór porównań i logiki co `filter`;
- pola są zapisywane zwykłą nazwą, parametry zawsze jako `params.<nazwa>`;
- nazwy `query` i `mutation` współdzielą przestrzeń operacji jednego modelu.

## Diagnostyki przy kodzie autora

Projektor zachowuje lokalizację storage, parametrów, przypisań i predykatu.
Problemy `SMPxxx` wskazują więc właściwy fragment `.jnx`:

- `SMP070` — nieprzenośny typ parametru;
- `SMP072`/`SMP073` — niedozwolony kształt `where`;
- `SMP074` — niedozwolona wartość `set`;
- `SMP080` — kolizja nazwy query/mutation;
- problemy walidatora M14.1, między innymi `SMP036`, `SMP038`, `SMP040` i
  `SMP041`, są mapowane na deklarację, `set` albo `where`.

IDE rozpoznaje nowe słowa w completion, hover i semantic tokens, a Outline
pokazuje mutation jako dziecko modelu `storage`. M14.2 nie tworzy jeszcze
wywoływalnej funkcji mutation w tasku.

## Czego M14.2 nie obiecuje

- wywołania mutacji z `task`;
- wykonania deskryptora Storage Runtime przez Host;
- zmiany danych w memory, SQLite lub PostgreSQL;
- transakcji wielooperacyjnych, upsertu i raw SQL;
- wykonania wyniku `affectedRows` przez provider ani polityki retry.

Od M14.3 kod może zostać sparsowany, zwalidowany i obniżony do neutralnego
deskryptora `mutation.plan`, ale nadal nie jest fizycznie wykonywany przez Host
ani provider w granicy samego M14.2. Od M14.4 istnieje wykonanie przez memory,
lecz bez wywołania mutation z tasku. Portal i CLI muszą tę granicę komunikować
wprost. Kontrakt loweringu opisuje
[`57-storage-runtime-mutation-lowering.md`](57-storage-runtime-mutation-lowering.md).

## Bramka odbioru

```powershell
./scripts/verify-m14-2.ps1
```

Bramka buduje rozwiązanie i uruchamia testy Core oraz workspace LSP. Pokrywa
publiczną ścieżkę diagnostyczną, trzy rodzaje mutacji,
zasięg `single/many`, source map, kolizję z query, nieprzenośny parametr,
niedozwolone `set` oraz lokalizację niebezpiecznego `where`.
