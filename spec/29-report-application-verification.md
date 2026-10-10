# Odbiór aplikacji `@report`

> **Identyfikator:** `junexis.report-application-verification/0.1`
>
> **Status:** VERIFIED
>
> **Kamień milowy:** M10
>
> **Data odbioru:** 2026-08-21

## Teza dowodu

Poprawny projekt z zadaniem `@report` tworzy użyteczną aplikację WWW, którą
można uruchomić razem z serwerem i przekazać jako gotowy artefakt. Autor nie
musi ręcznie przygotowywać HTML, bundlera ani kodu startowego.

```text
projekt Junexis
    │
    ├── @server ── WebSocket + MessagePack ──┐
    │                                        │
    └── @report ── generator aplikacji ──────┤
                                             ▼
                              panel w prawdziwej przeglądarce
                              auto / deklaratywny / własny
```

## Macierz kryteriów odbioru

| # | Kryterium | Wynik | Dowód |
|---:|---|---|---|
| 1 | pusty `@report` tworzy użyteczny panel | PASS | testy generatora aplikacji i Golden Home Report |
| 2 | artefakt działa w prawdziwej przeglądarce | PASS | Chrome E2E |
| 3 | dane przechodzą z uruchomionego serwera | PASS | rzeczywisty WebSocket + MessagePack w Golden E2E |
| 4 | rozłączenie i reconnect są widoczne | PASS | scenariusze `stale`, `reconnecting`, `rejoined` |
| 5 | obca wersja kontraktu jest odrzucana | PASS | test stanu `incompatible` |
| 6 | historia jest ograniczona | PASS | limit `50` i test dużych danych |
| 7 | paczka działa bez checkoutu i toolchainu | PASS | System Bundle `server + report` uruchomiony z rozpakowanego ZIP |
| 8 | adapter autora nie jest nadpisywany | PASS | test ownership `user-overlay` i ponowny build |
| 9 | zależności autora nie przejmują builda | PASS | walidowane scalenie manifestu DOM/React/Vue |
| 10 | widok deklaratywny używa istniejących zadań i pól | PASS | walidacja `view.json` i diagnostyka `B0005` |
| 11 | motywy i klawiatura są sprawdzone | PASS | audyt Chrome: jasny/ciemny, fokus, 390 px, reduced motion |
| 12 | Dom 11 pokazuje `BoundaryObservation` | PASS | Golden Home Report E2E |

## Dowody wykonywalne

- `compiler/JunexisLang.Compiler.CodeGen.Tests/CodeGeneratorReportApplicationTests.cs`
  — generowany kontrakt, renderer, lifecycle, CSP i limit danych,
- `compiler/JunexisLang.Compiler.CLI.Tests/ReportScaffoldCommandTests.cs`
  — widok deklaratywny, adaptery, zależności i ownership,
- `examples/projects/11-distributed-recovery/evidence/browser-e2e-2026-08-14.md`
  — rzeczywista ścieżka serwer–przeglądarka i reconnect,
- `examples/projects/11-distributed-recovery/evidence/browser-quality-2026-08-14.md`
  — dostępność i bezpieczeństwo w Chrome,
- `examples/projects/12-modular-storage-golden/evidence/system-bundle-verification-2026-08-21.md`
  — gotowy panel i natywny serwer uruchomione z paczki bez checkoutu.

Ostatni dowód ma Build ID
`345445370763cd03cf81fa9cc976fdc79c393f02c3b0352656f208d98f6b32fd`
i Bundle ID
`4bb3b2db62dccd9671ae187385ebc3badb98c62eac09a5779c3308da4fe5a63f`.

## Zakres słowa VERIFIED

`VERIFIED` oznacza domknięcie kontraktu M10, a nie kompletność całego backendu
TypeScript ani stabilność Junexis 1.0. Test przeglądarkowy obejmuje Chrome na
Windows x64. Czytnik ekranu, Firefox, WebKit, forced colors, kolejne paczki
systemowe, składnia widoku w `.jnx` i projektant wizualny są rozszerzeniami po
M10. Nie unieważniają odebranego pionu.

## Relacja z M11

M11 nie zmienia semantyki aplikacji raportowej. Golden Modular Storage używa
jej jako końca dłuższej ścieżki:

```text
moduły .jnx → HomeStore → SQLite → restart → @server → @report
```

Jest to dodatkowy dowód, że aplikacja M10 jest samodzielnym komponentem
pełnego, pakowalnego systemu, a nie wyłącznie demonstracją generatora UI.
