# M13.4 — wykonanie Query Plan przez provider memory

> **Status:** VERIFIED EXECUTION SLICE
>
> **Zakres:** read-only Query Plan 0.1, Storage Runtime ABI 0.1, provider memory
>
> **Poza zakresem:** SQLite, PostgreSQL, mutacje, relacje i agregacje

## Co zostało domknięte

M13.1 ustalił model i algebrę, M13.2 dał składnię `.jnx`, a M13.3 utworzył
neutralne wywołanie. M13.4 po raz pierwszy wykonuje tę samą intencję za granicą
procesu providera:

```text
storage/query w .jnx
        │ M13.2
        ▼
model + Query Plan IR
        │ M13.3
        ▼
query.plan / read
        │ Storage Runtime Host
        ▼
provider memory [query-plan]
        │ scan → filter → project → sort → limit
        ▼
typowany kursor wyniku
```

Host zna nazwę operacji i wymaga capability `query-plan`, ale nie zna węzłów
algebry ani modelu danych. Provider zależy wyłącznie od publicznej paczki
`Junexis.Storage.Runtime.Abstractions`; nie importuje kompilatora i nie zna
generatora `@server`. Ta granica zachowuje decyzję D21.

## Referencyjne mapowanie memory

M13.4 potrzebuje uczciwego, małego mapowania fizycznego. Bucket o nazwie encji
logicznej reprezentuje jej zbiór, a każdy `payload` jest obiektem JSON zgodnym
z polami modelu:

```text
entity Observation
    ↕
bucket "Observation"
    ├── key "one"   → payload { id, homeId, sensor, value, recordedAt }
    └── key "two"   → payload { id, homeId, sensor, value, recordedAt }
```

Klucz techniczny bucketu nie staje się automatycznie polem encji. Klucz
logiczny, na przykład `id`, pozostaje jawnym polem payloadu i podlega modelowi.
To mapowanie służy jako referencyjny dowód semantyki, a nie jako uniwersalne
mapowanie relacyjne.

## Semantyka wykonania 0.1

Provider:

1. sprawdza wersje invocation, modelu i planu;
2. wiąże wszystkie argumenty z deklarowanymi parametrami;
3. tworzy snapshot rekordów encji z bindingu lub aktywnej transakcji;
4. sprawdza przenośne typy rekordów;
5. wykonuje kroki w kolejności zapisanej przez lowerer;
6. sprawdza końcowy shape i oddaje wiersze przez kursor ABI.

Obsługiwany pion obejmuje dziewięć typów Storage ABI, wyrażenia pola,
parametru i literału, porównania, `and`, `or`, `not`, kontrolę null oraz kroki
`filter`, `project`, `sort` i `limit`. Porównanie z nullem nie przechodzi
filtra; do jawnego sprawdzenia służy predykat null. Sort jest stabilny i
deterministyczny, a w porządku rosnącym null znajduje się za wartością.

Provider ponownie waliduje niezaufaną kopertę mimo wcześniejszej kontroli
kompilatora. Błędna wersja, argument, shape lub rekord przekracza granicę jako
portable `constraint`, bez ujawniania wewnętrznego wyjątku.

## Czego M13.4 nie dowodzi

- nie tłumaczy planu na SQL;
- nie wykonuje planu przez SQLite ani PostgreSQL;
- nie definiuje mapowania tabel, indeksów lub migracji;
- nie wprowadza `insert`, `update`, `delete`, joinów ani agregacji;
- nie optymalizuje kolejności kroków;
- nie podłącza jeszcze wygenerowanego klienta konkretnego targetu do tego
  scenariusza jako pełnego E2E `.jnx → działający system`.

## Dowód

Test Runtime potwierdza, że Host odrzuca `query.plan` bez capability.
Test providera zapisuje trzy obserwacje i przez Host wykonuje filtr UUID,
projekcję, sort malejący i limit. Wynik ma deklarowane typy oraz kończy się
kontrolowanym `done=true`. Drugi test uruchamia spakowany Host i provider w
osobnym procesie, po czym pobiera ten sam typowany wynik przez kursor.

Conformance semantyki planu i wykonanie go przez provider SQLite bez zmiany
dokumentu tworzonego przez kompilator zostały zrealizowane w M13.5; zob.
[`46-storage-sqlite-query-conformance.md`](46-storage-sqlite-query-conformance.md).
