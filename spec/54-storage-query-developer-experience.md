# M13.13 — ergonomia developera i zamknięcie ścieżki odczytu

> **Status:** VERIFIED STORAGE QUERY DEVELOPER EXPERIENCE
>
> **Zakres:** CLI, workspace LSP i powtarzalne piony M13.10–M13.12

## Cel

Wykonywalne named query nie wystarczy, jeżeli developer musi pamiętać
syntetyczne nazwy albo ręcznie szukać deklaracji w innym pliku. M13.13 dodaje
widok IDE wyprowadzany z tego samego zakresu importów, który sprawdza semantyka.

Dla kodu:

```junexis
import data::home { HomeData as Store }

task ReadHome(homeId: Uuid) @server
    -> List<Store_RecentObservationsRow> {
    return Store_RecentObservations(homeId: homeId)
}
```

LSP zna zarówno `Store_RecentObservations`, jak i
`Store_RecentObservationsRow`, mimo że moduł `data::home` nie został wcześniej
otwarty w edytorze.

## Completion

Lista podpowiedzi zawiera:

- funkcje named query widoczne w bieżącym zakresie importów;
- syntetyczne typy wierszy;
- lokalne aliasy API storage;
- słowa DSL `storage`, `entity`, `query`, `filter`, `project`, `sort`, `limit`,
  `offset`, `key`, `asc` i `desc`;
- przenośne typy `Bytes`, `Decimal`, `Uuid`, `Json`, `Null`, `List`, `Option`
  i `Result`.

Symbol spoza zakresu modułu nie jest proponowany przypadkiem tylko dlatego, że
istnieje w innym pliku workspace.

## Hover

Hover funkcji pokazuje:

- pełną sygnaturę i typ `List<...Row>`;
- efekt read-only oraz ograniczenie do tasku `@server`;
- kanoniczną nazwę modelu Runtime;
- mapowanie lokalnego aliasu na model kanoniczny.

Hover typu wiersza pokazuje końcowy shape po wszystkich krokach `project`,
łącznie z aliasami pól.

## Go to Definition i Outline

Go to Definition z funkcji query albo syntetycznego typu wiersza prowadzi do
źródłowej deklaracji `query`, również w nieotwartym module. Panel Outline
pokazuje deklarację `storage`, jej encje i named query.

Alias całego modułu nadal nie ma składni kwalifikowanego wywołania query:

```junexis
import data::home as data
```

Binder emituje wtedy ostrzeżenie `SQC006` i zaleca selective import modelu.
Brak re-exportowanego symbolu nadal daje istniejące `E0007` w miejscu importu.

## Macierz zamknięcia M13

| Etap | Osiągnięty kontrakt |
|---|---|
| M13.1 | logiczny model danych i Query Plan IR |
| M13.2 | składnia `storage` i projekcja źródła |
| M13.3 | neutralne wywołanie Storage Runtime ABI |
| M13.4 | wykonanie przez provider memory |
| M13.5 | conformance SQLite |
| M13.6 | bezpieczny SQLite SQL pushdown |
| M13.7 | wykonanie PostgreSQL |
| M13.8 | bezpieczny PostgreSQL SQL pushdown |
| M13.9 | generowany typowany klient Go |
| M13.10 | bezpośrednie wywołanie z tasku `@server` |
| M13.11 | model storage w importowanym module |
| M13.12 | lokalny alias z kanoniczną tożsamością Runtime |
| M13.13 | completion, hover, definition, Outline i końcowa bramka |

## Bramka odbioru

```powershell
./scripts/verify-m13-13.ps1
```

Bramka buduje rozwiązanie, uruchamia testy Core, CLI i workspace LSP, a potem
powtarza trzy końcowe piony wykonawcze: lokalny task M13.10, importowany model
M13.11 i alias M13.12. Każdy pion używa rzeczywistego Storage Runtime Host oraz
providera memory.

Historyczne live dowody SQLite i PostgreSQL pozostają przypisane odpowiednio do
M13.5–M13.8; PostgreSQL wymaga jawnego środowiska integracyjnego i nie jest
udawany w lokalnej bramce IDE.

## Stan końcowy

M13 zamyka przenośną ścieżkę odczytu:

```text
storage/query → semantyka → Query Plan → Runtime Host → provider
      ↑                                                    │
      └──── IDE, typowany task i syntetyczny wynik ────────┘
```

Mutacje, jawne wyniki błędów, transakcje aplikacyjne i raw SQL należą do
następnych kamieni milowych. Nie są ukrytym zakresem M13.
