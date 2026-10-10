# M13.12 — lokalny alias modelu storage

> **Status:** VERIFIED ALIAS-TO-CANONICAL E2E
>
> **Tryb pracy:** projekt z `junexis.project.toml`
>
> **Target wykonawczy:** `@server` / Go

## Idea

Alias ma poprawiać czytelność modułu, ale nie może przemianować danych w całym
systemie. M13.12 rozdziela więc dwie nazwy:

| Warstwa | Nazwa |
|---|---|
| kod bieżącego modułu | lokalny alias `Store` |
| wspólny IR, Query Plan i Runtime | kanoniczna nazwa `HomeData` |

```junexis
import data::home { HomeData as Store }

task ReadHome(homeId: Uuid) @server
    -> List<Store_RecentObservationsRow> {
    return Store_RecentObservations(homeId: homeId)
}
```

Developer pracuje z krótszym `Store_...`. Po sprawdzeniu semantyki kompilator
obniża te symbole do:

```text
Store_RecentObservations     → HomeData_RecentObservations
Store_RecentObservationsRow  → HomeData_RecentObservationsRow
```

Ta zamiana zachodzi przed zbudowaniem wspólnego IR. Dzięki temu alias nie
zmienia nazwy modelu w kopercie `query.plan`, fingerprintu, wygenerowanego
klienta ani bindingu providera.

## Semantyka

Selective import z aliasem deklaracji `storage` udostępnia pod aliasem całe
pochodne API named query:

- każdą funkcję `Alias_Query(...)`;
- każdy syntetyczny typ `Alias_QueryRow`;
- te same parametry, typy wyniku i ograniczenie do tasku `@server`.

Alias obowiązuje tylko w module, który go zadeklarował. Inny moduł może używać
kanonicznej nazwy albo własnego aliasu bez wpływu na Runtime.

## Dlaczego alias nie trafia do Runtime

Gdyby `Store` stało się nazwą modelu wire, ten sam model źródłowy mógłby
otrzymywać różne fingerprinty zależnie od miejsca importu. Provider widziałby
pozornie różne kontrakty, mimo że ich encje i query są identyczne. M13.12
usuwa tę dwuznaczność: alias jest elementem ergonomii kodu, nie schematu danych.

## Dowód Golden

```powershell
./scripts/verify-m13-12.ps1
```

Bramka buduje projekt
[`14-aliased-storage-query`](../examples/projects/14-aliased-storage-query),
w którym task używa wyłącznie symboli `Store_...`. Następnie kompiluje
wygenerowany Go, uruchamia rzeczywisty Storage Runtime Host i provider memory,
zapisuje obserwację oraz odczytuje ją przez task. Testy generatora dodatkowo
potwierdzają, że artefakty wykonawcze zawierają wyłącznie kanoniczne symbole
`HomeData_...`.

## Granice

M13.12 nie dodaje:

- aliasu całego modułu, np. `import data::home as data`, dla wywołań query;
- składni kwalifikowanej `data::HomeData_Query(...)`;
- re-exportu aliasu do następnego modułu;
- zmiany kanonicznej nazwy modelu lub migracji jego tożsamości;
- mutacji i obsługi błędów query w kodzie tasku.

Alias jest lokalnym widokiem API. Własność danych nadal określa deklaracja
`storage HomeData` w module definiującym.

Completion, hover i Go to Definition dla lokalnego aliasu realizuje
[`54-storage-query-developer-experience.md`](54-storage-query-developer-experience.md).
