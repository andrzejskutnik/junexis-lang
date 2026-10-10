# Junexis Storage 0.1 — kontrakt providera

> **Identyfikator:** `junexis.storage/0.1`  
> **Status:** VERIFIED  
> **Kamień milowy:** M11.7  
> **Zakres:** semantyka wspólna dla memory, SQLite, PostgreSQL i kolejnych providerów

## Po co istnieje ten kontrakt

Kod domenowy Junexis nie powinien zależeć od tego, czy dane są przechowywane w
SQLite, pamięci procesu czy innym systemie. Provider może zmienić technologię,
ale nie może zmienić znaczenia otwarcia zasobu, zapisu, transakcji ani błędu.

`junexis.storage/0.1` jest wspólną umową między:

- programem Junexis,
- analizą i generatorami,
- natywnym adapterem providera,
- runtime,
- testami zgodności i narzędziami diagnostycznymi.

M11.7 zamraża tę umowę. Nie twierdzi jeszcze, że SQLite lub provider
pamięciowy ją realizują — te dowody należą odpowiednio do M11.8 i M11.9.

## Warstwa bootstrapowa

Do czasu wprowadzenia pełnej składni `resource` źródłem kontraktu jest istniejąca
deklaracja `extern storage`:

```junexis
extern storage HomeStore {
    target server
    language go
    file "user/server/home_store.go"

    fn open()
    fn save(observation: HomeObservation) -> SaveResult
    fn latest(room: Text, limit: Int) -> [HomeObservation]
    fn begin_transaction()
    fn commit()
    fn rollback()
    fn migrate()
    fn health_check() -> Bool
    fn close()
}
```

Kompilator projektuje tę granicę do neutralnego Semantic IR i podczas
generowania targetu serwerowego zapisuje:

```text
server/storage/junexis.storage.contracts.json
```

Manifest ma wersję `junexis.storage/0.1`. Nie zawiera konfiguracji połączenia,
sekretów ani danych rekordów. Opisuje nazwę providera, jego binding, operacje,
efekty oraz wspólne reguły wykonania.

## Lifecycle

Każdy uchwyt zasobu przechodzi przez jawne stany:

```text
closed → open → transaction-active → open → closing → closed
```

Reguły 0.1:

1. stan początkowy to `closed`,
2. operacje danych wymagają stanu `open` lub aktywnej transakcji,
3. w jednym uchwycie może istnieć najwyżej jedna aktywna transakcja,
4. `close` czeka na operacje w toku albo je anuluje zgodnie z konfiguracją,
5. `close` nie może po cichu zatwierdzić aktywnej transakcji; musi ją wycofać,
6. ponowne otwarcie tworzy poprawny uchwyt albo zwraca sklasyfikowany błąd.

Lifecycle jest obowiązkiem providera nawet wtedy, gdy operacje `open` i `close`
nie są jeszcze jawnie zadeklarowane przez program. Jawne funkcje pozwalają
narzędziom powiązać je z operacjami domenowymi.

## Efekty operacji

Semantic IR rozróżnia efekty:

| Efekt | Znaczenie |
|---|---|
| `lifecycle` | otwarcie lub zamknięcie zasobu |
| `read` | odczyt bez zmiany trwałego stanu |
| `write` | zapis albo usunięcie zmieniające stan |
| `transaction` | begin, commit lub rollback |
| `migration` | zmiana wersji schematu |
| `health` | sprawdzenie gotowości zasobu |
| `custom` | operacja domenowa wymagająca przyszłego jawnego oznaczenia |

W okresie bootstrapowym rodzaj jest wyprowadzany z kanonicznych nazw. Na
przykład `save` i `update_*` są zapisem, `read_*`, `latest` i `recent` są
odczytem, a `begin_transaction`, `commit` i `rollback` sterują transakcją.
Nierozpoznana nazwa otrzymuje `custom`; kompilator nie udaje wtedy, że zna jej
efekt. Docelowa składnia `resource` zastąpi tę konwencję jawną deklaracją.

## Wynik operacji i klasy błędów

Operacja kończy się wynikiem `ok` albo `error`. Błąd providera jest mapowany na
jedną ze stabilnych klas:

| Kod | Ponowienie może mieć sens | Znaczenie wyniku |
|---|---:|---|
| `not-found` | nie | brak żądanej wartości |
| `conflict` | nie | konflikt wersji lub stanu |
| `constraint` | nie | naruszenie reguły danych |
| `busy` | tak | zasób chwilowo zajęty |
| `timeout` | tak | przekroczono ograniczony czas |
| `cancelled` | tak | operacja została anulowana |
| `migration` | nie | migracja nie może zostać bezpiecznie zastosowana |
| `unavailable` | tak | provider jest chwilowo niedostępny |
| `unknown-outcome` | nie automatycznie | nie wiadomo, czy skutek został utrwalony |
| `internal` | nie automatycznie | niesklasyfikowany błąd providera |

`retryable` nie oznacza automatycznego ponowienia. Polityka retry musi znać
idempotencję operacji i budżet czasu. W szczególności zapis z wynikiem
`unknown-outcome` wymaga najpierw uzgodnienia stanu, aby nie wykonać skutku dwa
razy.

M14.3 konkretyzuje kanał sukcesu dla Mutation Plan jako
`junexis.storage.mutation-result/0.1`: `applied` oznacza potwierdzony skutek i
dodatnie `affectedRows`, a `not-applied` poprawne zakończenie bez zmiany i zero
rekordów. `unknown-outcome` celowo pozostaje błędem ABI, a nie trzecim wariantem
sukcesu. Szczegóły określa
[`57-storage-runtime-mutation-lowering.md`](57-storage-runtime-mutation-lowering.md).

## Transakcja

Granica transakcji składa się z `begin`, `commit` i `rollback`. Jej wynik jest
jednym z:

- `committed` — provider potwierdził trwałe zatwierdzenie,
- `rolled-back` — provider potwierdził brak skutku transakcji,
- `unknown` — runtime utracił możliwość rozstrzygnięcia wyniku.

Transakcje zagnieżdżone nie należą do kontraktu 0.1. Przerwanie po wysłaniu
`commit` może dać `unknown`; nie wolno przedstawiać go użytkownikowi jako
pewnego rollbacku ani pewnego sukcesu.

## Timeout i anulowanie

Każda operacja blokująca musi mieć ograniczony czas wynikający z konfiguracji
providera. Anulowanie jest kooperacyjne: provider ma przerwać oczekiwanie,
zwolnić zasoby i zwrócić sklasyfikowany wynik. Anulowanie nie może zmieniać
nieznanego wyniku zatwierdzenia w pozornie bezpieczny `cancelled`.

## Gwarancje providera 0.1

Provider deklarujący zgodność musi dowieść:

1. wartości trafiają do zapytań przez parametry, a nie konkatenację tekstu,
2. potwierdzony zapis jest trwały zgodnie z profilem providera,
3. granica transakcji jest jawna,
4. zastosowana migracja jest niezmienna,
5. oczekiwanie na blokadę jest ograniczone,
6. operacje reagują na anulowanie,
7. telemetria nie ujawnia sekretów ani pełnej treści rekordów.

Provider pamięciowy może uczciwie deklarować brak trwałości po restarcie, ale
musi zachować znaczenie operacji, transakcji i błędów. Dzięki temu testuje logikę
domenową, a nie udaje SQLite.

M11.9 materializuje ten wymóg jako wspólny interfejs wykonawczy `storage.Store`.
Memory, SQLite i PostgreSQL implementują te same operacje `Put`, `Get`, `List`,
`Delete` i `Transact`; różnica trwałości oraz granicy procesu pozostaje jawną
właściwością providera. Implementację PostgreSQL i jej bieżący poziom dowodu
opisuje [`30-postgresql-provider.md`](30-postgresql-provider.md).

## Granice odpowiedzialności

- `@report` nie otwiera pliku bazy; komunikuje intencję do `@server`.
- `check` i analiza nie tworzą bazy ani nie wykonują migracji.
- manifest kontraktu nie jest konfiguracją deploymentu.
- ścieżki, sekrety, limity i polityka synchronizacji należą do bindingu
  środowiska, nie do źródła danych domenowych.
- własny provider musi przejść ten sam zestaw testów zgodności co provider
  referencyjny.

## Dowód M11.7

Etap jest zweryfikowany przez:

- typy Semantic IR dla rodzaju operacji i efektu storage,
- projekcję wyłącznie z deklaracji `extern storage`,
- klasyfikację lifecycle, read, write, transaction, migration i health,
- generowany manifest JSON z lifecycle, wynikiem, błędami, timeoutem,
  transakcją i gwarancjami,
- test, że zwykły kontrakt `extern` nie tworzy manifestu storage.

M11.8 zaczyna się od implementacji SQLite pod ten kontrakt, a nie od ponownego
projektowania jego znaczenia.
