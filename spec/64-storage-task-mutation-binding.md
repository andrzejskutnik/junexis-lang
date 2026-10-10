# M14.10 — named mutation wywoływana z tasku

> **Status:** VERIFIED TASK-TO-MUTATION E2E
>
> **Target wykonawczy:** `@server` / Go
>
> **Pion:** `task .jnx → named mutation → Mutation Plan → Runtime Host → provider`

## Idea

M14.9 udostępnił bezpiecznego klienta kodowi hosta. M14.10 pozwala użyć tej
samej operacji bez opuszczania języka Junexis:

```junexis
task RegisterObservation(id: Uuid, sensor: Text) @server
    -> HomeData_AddObservationResult {
    return HomeData_AddObservation(sensor: sensor, id: id)
}
```

Developer nie otwiera Hosta, nie buduje JSON i nie importuje kodu Go.
Kompilator syntetyzuje symbole `HomeData_AddObservation(...)` oraz
`HomeData_AddObservationResult`, wiąże wywołanie z deklaracją mutation i
generuje połączenie z klientem M14.9.

## Typowanie i zasięg

Argumenty mogą być pozycyjne w kolejności deklaracji albo nazwane w dowolnej
kolejności. Semantyka sprawdza liczbę, nazwy i typy argumentów oraz dopuszcza
wywołanie wyłącznie wewnątrz jednoznacznego tasku `@server`. Nazwane argumenty
są porządkowane przed generowaniem backendu.

Wynik językowy ma dwa pola:

```text
outcome: Text       // applied | not-applied
affectedRows: Int
```

Nie jest to zwracany rekord encji. To potwierdzenie skutku operacji. Zasady
`single/many`, komplet klucza i bezpieczeństwo insert/update/delete nadal
pochodzą z deklaracji Mutation Plan, a nie z tasku.

## Lifecycle i błędy

Jeżeli choć jeden task używa named query lub mutation, wygenerowany serwer
otwiera jeden provider wskazany przez deployment i zamyka go razem z procesem.
Task nie zna nazwy providera ani connection stringa.

Bootstrap 0.2 zachowuje dotychczasowe fail-fast: błąd Hosta, providera albo
`unknown-outcome` zatrzymuje bieżące wykonanie przez panic. Nie jest zamieniany
na `not-applied` i nie uruchamia automatycznego retry. Jawne przechwytywanie
błędów w kodzie Junexis wymaga osobnego modelu `Result`/efektów.

## Diagnostyki

Rodzina `SMC001–SMC005` obejmuje użycie poza `@server`, kształt argumentów,
obcą nazwę oraz niezgodność typu. Zwykła kontrola zwracanego typu pilnuje, aby
task deklarował właściwy `...Result`.

## Dowód Golden

```powershell
./scripts/verify-m14-10.ps1
```

Bramka kompiluje
[`26-storage-mutation-from-task.jnx`](../examples/grammar/26-storage-mutation-from-task.jnx),
uruchamia Runtime Host i provider memory, wywołuje task
`RegisterObservation`, sprawdza `applied/1` oraz fizycznie odczytuje utworzony
rekord. Test nie wywołuje pakietu mutacji Go bezpośrednio.

## Granica M14.10

Etap nie obejmuje jeszcze named mutation z importowanego modelu storage,
aliasowania jego API, wsparcia IDE dla pochodnych symboli mutation, transakcji
wielu operacji, jawnej obsługi błędu w tasku, idempotency key, raw SQL ani
targetów `@embedded` i `@report`.
