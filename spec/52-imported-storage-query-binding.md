# M13.11 — named query z importowanego modułu storage

> **Status:** VERIFIED MODULAR TASK-TO-STORAGE E2E
>
> **Tryb pracy:** projekt z `junexis.project.toml`
>
> **Target wykonawczy:** `@server` / Go

## Idea

Model danych nie powinien rosnąć w tym samym pliku co zachowanie całej
aplikacji. M13.11 pozwala umieścić deklarację `storage` w module danych i użyć
jej typowanego API w module aplikacji:

```junexis
// src/data/home.jnx
storage HomeData {
    entity Observation {
        id: Uuid
        homeId: Uuid
        sensor: Text
        key(id)
    }

    query ByHome(homeId: Uuid) from Observation {
        filter homeId == params.homeId
        project sensor
    }
}
```

```junexis
// src/main.jnx
import data::home { HomeData }

task ReadHome(homeId: Uuid) @server -> List<HomeData_ByHomeRow> {
    return HomeData_ByHome(homeId: homeId)
}
```

Importowany jest logiczny model `HomeData`. Jego funkcje query i typy wierszy
są API pochodnym, więc developer nie powtarza ich w pliku aplikacji.

## Reguły importu

M13.11 obsługuje dwa warianty zachowujące kanoniczną nazwę modelu:

```junexis
import data::home
import data::home { HomeData }
```

Oba udostępniają `HomeData_ByHome(...)` oraz `HomeData_ByHomeRow`. Selective
import jest zalecany, ponieważ pokazuje zależność modułu aplikacji bez
zanieczyszczania jego zakresu pozostałymi deklaracjami.

M13.11 nie obejmował aliasu modelu. Granicę tę domknął M13.12: `HomeData as
Store` udostępnia lokalne `Store_ByHome`, ale przed wspólnym IR jest ono
obniżane do kanonicznego `HomeData_ByHome`. Alias nie zmienia fingerprintu ani
tożsamości wire. Szczegóły opisuje
[`53-storage-import-alias.md`](53-storage-import-alias.md).

## Co robi kompilator

1. resolver buduje graf modułów projektu;
2. binder eksportuje deklarację `storage` do zakresu importującego modułu;
3. semantyka syntetyzuje z importowanego modelu te same funkcje query i typy
   wierszy co dla deklaracji lokalnej;
4. composer układa osiągalne moduły dependency-first;
5. codegen tworzy jeden provider-neutral klient oraz wrapper używany przez
   task wejściowy;
6. deployment nadal wybiera Runtime Host i konkretny provider.

Walidacja argumentów, ograniczenie do tasku `@server`, typ wyniku i lifecycle
są identyczne jak w M13.10. Import nie osłabia kontraktu semantycznego.

## Dowód Golden

```powershell
./scripts/verify-m13-11.ps1
```

Bramka buduje projekt
[`13-imported-storage-query`](../examples/projects/13-imported-storage-query),
sprawdza manifest modułów, kompiluje wygenerowany Go i uruchamia task
`ReadHome`. Zapytanie przechodzi następnie przez rzeczywisty Storage Runtime
Host i provider memory. Test zapisuje rekord poza wygenerowanym taskiem, a
odczytu dokonuje wyłącznie przez API zaimportowanego named query.

## Granice

M13.11 nie dodaje:

- aliasowania nazwy modelu storage — granicę tę zrealizował M13.12;
- re-exportu query przez moduł pośredni;
- przejścia LSP „do definicji” z syntetycznej funkcji do deklaracji query —
  granicę tę zrealizował M13.13;
- mutacji, relacji, joinów, agregacji ani migracji;
- klientów query dla `@embedded` i `@report`;
- wyboru providera w kodzie modułu.

Modularność porządkuje własność kontraktu. Nie zmienia architektury wykonania:
aplikacja zależy od modelu i named query, Runtime od wersjonowanego ABI, a
provider od fizycznego silnika danych.
