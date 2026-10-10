# M13.1 — abstrakcyjny model danych i Query Plan IR

> **Status:** VERIFIED COMPILER CONTRACT
>
> **Składnia języka:** dodana w M13.2; zob. [`43-storage-source-projection.md`](43-storage-source-projection.md)
>
> **Wykonanie przez provider:** pierwszy pion memory dodany w M13.4

## Idea

M12 dał Junexis niezależne providery i wspólne ABI. M13 zaczyna odpowiadać na
inne pytanie: jak opisać zamiar pracy z danymi, zanim wybierzemy SQLite,
PostgreSQL, pamięć albo konkretny dialekt SQL?

M13.1 wprowadza dwie neutralne warstwy:

```text
logiczny model danych
  Observation { id, homeId, sensor, value, recordedAt }
                         │
                         ▼
Query Plan IR
  scan → filter → project → sort → limit
                         │
                 późniejsze etapy M13
                         ▼
provider lowering
  memory | SQLite | PostgreSQL | przyszły provider
```

Model mówi, jakie dane znaczą coś w systemie. Plan mówi, co chcemy z nimi
zrobić. Dopiero provider zdecyduje, jak tę intencję wykonać.

## Logiczny model danych 0.1

Kontrakt `junexis.storage.model/0.1` opisuje:

- nazwę modelu;
- encje logiczne;
- pola i ich przenośne typy;
- nullability;
- jawny, jedno- lub wielopolowy klucz główny.

Typy M13.1 odpowiadają przenośnym wartościom Storage ABI: `int64`, `float64`,
`bool`, `text`, `bytes`, `decimal`, `timestamp`, `uuid` i `json`.

Model nie zawiera nazw tabel, kolumn, schematów PostgreSQL, indeksów ani nazw
klas sterownika. Takie szczegóły należą do mapowania providera i deploymentu.

## Query Plan IR 0.1

Kontrakt `junexis.storage.query-plan/0.1` zaczyna od kompletnego, ale celowo
małego pionu odczytu:

| Węzeł | Znaczenie |
|---|---|
| `scan` | wybierz logiczną encję źródłową |
| `filter` | pozostaw rekordy spełniające typowany warunek |
| `project` | zbuduj jawny kształt wyniku |
| `sort` | uporządkuj wynik po jednym lub wielu kluczach |
| `limit` | ogranicz liczbę rekordów i opcjonalnie ustaw offset |

Wyrażenia mogą odwoływać się do pola, typowanego parametru albo literału w
postaci kanonicznej. Warunki wspierają porównania, `and`, `or`, `not` oraz
kontrolę wartości null.

Przykładowa intencja:

```text
plan RecentObservations(homeId: uuid)
  limit 20
    sort recordedAt descending
      project sensor, value, recordedAt
        filter homeId == $homeId
          scan Observation
```

To nie jest składnia `.jnx`, lecz zapis struktury IR. Od M13.2 deklaracja
źródłowa `storage` jest projektowana właśnie do tej reprezentacji.

## Co sprawdza kompilator

Walidator M13.1 wykrywa przed uruchomieniem providera:

- nieobsługiwaną wersję modelu albo planu;
- niezgodną nazwę modelu;
- powtórzone encje, pola, parametry i nazwy projekcji;
- brakujący, nieznany lub nullable klucz główny;
- odwołanie do nieznanej encji, pola albo parametru;
- porównanie wartości różnych typów;
- pusty sort, niepoprawny limit i ujemny offset;
- użycie pola usuniętego przez wcześniejszą projekcję.

Ostatnia reguła jest istotna edukacyjnie: każdy węzeł planu ma wejście i wynik.
`project` naprawdę zmienia kształt danych, więc następny węzeł widzi tylko pola,
które projekcja pozostawiła.

## Granica odpowiedzialności

M13.1 nie generuje SQL i nie wykonuje zapytania. Nie dodaje jeszcze:

- mutacji `insert`, `update`, `delete`;
- relacji, joinów i agregacji;
- mapowania modelu na fizyczny schemat bazy;
- optymalizacji planu;
- loweringu dla memory, SQLite ani PostgreSQL.

Dzięki temu kontrakt można ocenić i testować bez przedwczesnego zamrożenia
publicznej składni języka.

## Dowód M13.1

Implementacja znajduje się w `JunexisLang.Compiler.IR` i obejmuje model,
algebrę planu oraz kontrolowane problemy `SQPxxx`. Testy potwierdzają:

1. poprawny plan `scan → filter → project → sort → limit`;
2. brak szczegółów SQL i konkretnych providerów w kontrakcie;
3. wykrywanie nieznanych pól i parametrów oraz konfliktu typów;
4. walidację modelu, klucza, sortowania i limitu;
5. propagację kształtu danych pomiędzy węzłami planu.

Następny wykonany krok, M13.2, połączył ten kontrakt z jawną deklaracją programu
i projekcją bez utraty typów oraz lokalizacji źródłowych. Nie zmienia to zakresu
samego kontraktu M13.1. M13.3 dodał neutralny deskryptor wywołania Storage
Runtime opisany w
[`44-storage-runtime-query-lowering.md`](44-storage-runtime-query-lowering.md),
ale nadal nie ustanawia wykonania przez provider.
