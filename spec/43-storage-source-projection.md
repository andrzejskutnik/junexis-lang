# M13.2 — deklaracja `storage` i projekcja źródła

> **Status:** VERIFIED COMPILER CONTRACT
>
> **Zakres:** logiczny model danych i przenośne zapytania tylko do odczytu
>
> **Wykonanie przez provider:** poza M13.2

## Cel

M13.1 ustalił neutralny model danych i Query Plan IR. M13.2 daje developerowi
jawną składnię `.jnx`, z której kompilator tworzy te kontrakty bez SQL, nazw
tabel fizycznych, drivera i języka backendu.

```text
storage w .jnx
      │ parser → AST → Structural IR
      ▼
StorageSourceProjector
      ├── junexis.storage.model/0.1
      └── junexis.storage.query-plan/0.1
                         │
                         ▼
              M13.3 lowering → M13.4 provider memory
```

To jest granica intencji. Program opisuje dane i wynik odczytu, a nie sposób,
w jaki SQLite, PostgreSQL albo provider pamięciowy ma go wykonać.

## Minimalna składnia

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

    query RecentObservations(homeId: Uuid) from Observation {
        filter homeId == params.homeId and value > 0.0
        project sensor as sensorName, value, recordedAt
        sort recordedAt desc
        limit 20
    }
}
```

Deklaracja `storage` zawiera logiczne `entity` i nazwane `query`. Encja ma pola
oraz dokładnie jeden zapis `key(...)`; klucz może składać się z wielu pól.
Zapytanie wskazuje encję po `from` i wykonuje kroki w kolejności zapisanej przez
autora.

## Model typów 0.1

Źródłowe typy są mapowane na przenośne typy Storage ABI:

| Junexis | Model storage |
|---|---|
| `Int`, `i8`–`i64`, `u8`–`u32` | `int64` |
| `Float`, `f32`, `f64` | `float64` |
| `Bool` | `bool` |
| `Text` | `text` |
| `Bytes` | `bytes` |
| `Decimal` | `decimal` |
| `Time` | `timestamp` |
| `Uuid` | `uuid` |
| `Json` | `json` |
| `T | Null` | nullable `T` |

`u64` jest odrzucany, ponieważ kontrakt 0.1 nie potrafi zachować całego jego
zakresu w `int64`. Typy strukturalne, kolekcje i dowolne unie również nie są
jeszcze częścią tego pionu.

## Parametry i pola

W filtrze zwykła nazwa oznacza pole aktualnego rekordu. Parametr zapytania jest
zawsze jawny jako `params.<nazwa>`:

```junexis
filter homeId == params.homeId
```

Rozdzielenie jest celowe. Kod nie zmienia znaczenia po dodaniu pola o nazwie
takiej samej jak parametr, a diagnostyka może niezależnie wskazać nieznane pole
lub parametr.

## Kroki odczytu

| Krok | Znaczenie w M13.2 |
|---|---|
| `filter <warunek>` | porównania, `and`, `or`, `not`, pola, parametry i literały |
| `project a, b as nazwa` | jawny kształt oraz opcjonalne aliasy wyniku |
| `sort pole [asc\|desc]` | jeden lub wiele kluczy sortowania |
| `limit n [offset m]` | ograniczenie wyniku i opcjonalne przesunięcie |

Nie każdy poprawny językowo `expression` jest poprawnym filtrem storage.
M13.2 odrzuca na przykład arytmetykę, wywołania funkcji i dostęp do dowolnych
obiektów, zamiast próbować zgadywać zachowanie providera.

Kolejność kroków ma znaczenie. Po `project` następne kroki widzą wyłącznie pola
pozostawione przez projekcję. Dlatego:

```junexis
project value
sort recordedAt desc
```

jest błędem, bo `recordedAt` nie istnieje już w kształcie pośrednim.

## Diagnostyka

Projekcja źródła emituje kontrolowane diagnostyki kategorii `StorageQuery` i
zachowuje plik, linię oraz kolumnę deklaracji. Sprawdzane są między innymi:

- powtórzone modele, encje, pola i zapytania;
- brak klucza, wiele deklaracji klucza i niepoprawne pole klucza;
- nieprzenośny typ lub nullable klucz;
- nieznana encja, pole albo parametr;
- operator niewchodzący do algebry 0.1;
- błędny kształt po projekcji, sort i limit.

Po projekcji uruchamiany jest ten sam walidator M13.1. Składnia źródłowa nie ma
więc łagodniejszej ścieżki niż bezpośrednio zbudowany Query Plan IR.

## Słowa kontekstowe

`storage`, `entity`, `query`, `filter`, `project`, `sort`, `limit`, `offset`,
`key`, `asc` i `desc` są rozpoznawane jako słowa składni M13 w odpowiednim
kontekście. Pozostają dozwolone w istniejących pozycjach identyfikatorów, np.
`extern storage`, parametr `limit` i argument platformy `storage=...`. M13.2 nie
wprowadza przez przypadek niekompatybilności dla takich programów.

## Granice M13.2

Ten etap nie dodaje:

- `insert`, `update`, `delete` ani transakcji w składni;
- relacji, joinów, agregacji i podzapytań;
- mapowania encji na fizyczny schemat;
- wyboru lub konfiguracji providera;
- generowania SQL, optymalizacji i wykonania planu;
- zależności tasku od nazwanej deklaracji `storage`.

Te granice są częścią kontraktu, nie ukrytym brakiem. M13.2 kończy odcinek
`źródło → AST → Structural IR → model/query-plan`. M13.3 wykonał kolejny
krok: lowering do neutralnego wejścia Storage Runtime, opisanego w
[`44-storage-runtime-query-lowering.md`](44-storage-runtime-query-lowering.md).

## Dowód

Testy kompilatora potwierdzają:

1. zachowanie nullability, klucza, parametrów, aliasów i kolejności kroków;
2. dokładne mapowanie `params.<nazwa>` oraz lokalizacji źródłowych;
3. odrzucenie `u64` i arytmetyki poza algebrą planu;
4. walidację pola usuniętego przez wcześniejszą projekcję;
5. diagnostykę wielu kluczy i powtórzonej nazwy zapytania;
6. brak regresji istniejących zastosowań słów `storage`, `limit` i `key`.

Kompilowalny przykład znajduje się w
[`examples/grammar/22-storage-model-query.jnx`](../examples/grammar/22-storage-model-query.jnx).
