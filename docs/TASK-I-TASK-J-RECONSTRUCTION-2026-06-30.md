# Task I / Task J Reconstruction Report

Data: 2026-06-30
Repo: junexis-lang
Branch roboczy: feat/task-i-j-reconstruction

## Cel

Odtworzyć możliwie najdokładniej stan prac opisanych jako `Task I` oraz `Task J`, wskazać ich źródła w historii git i ocenić, czy da się je odzyskać jako dwa osobne stany kodu bez zgadywania brakujących zmian.

## Wynik skrócony

- `Task I` i `Task J` mają dziś jednoznaczny opis funkcjonalny w `CHANGELOG.md`.
- Najstarszy potwierdzony ślad tych prac w historii to commit `014d6b7`.
- W zwykłej historii `main`, w innych branchach i w reflogu nie ma osobnego commita `Task I only` ani `Task J only`.
- To oznacza, że dokładne odtworzenie dwóch osobnych historycznych stanów kodu nie jest możliwe wyłącznie przez checkout / cherry-pick istniejących commitów.
- Najbliższy dokładny stan źródłowy to commit agregujący `014d6b7`, a późniejsze commity `083e0a3`, `a14d981` i `f97a627` porządkują lub walidują ten rezultat.

## Najważniejsze źródła

### 1. Bieżący changelog

`CHANGELOG.md`, sekcja `Validation`:

- `Task I`: generated Go dla `03-smart-home` emituje trace do realnego TraceCollectora po dopasowaniu payloadu ingest (`kind` jako string event kind i zachowanie `attributes.narrative_id`).
- `Task I`: ścieżka `generated Go -> TraceCollector -> Why API` działa lokalnie, ale przy samych startup trace events głębokość chain pozostaje `1`.
- `Task J`: minimalny causal chain osiągnięto przez współdzielony `payloadHash` oraz pary `COMM_SEND` / `COMM_RECV`.
- `Task J`: poprawiono orkiestrację tasków zwracających prymitywy komunikacyjne i zaktualizowano golden tests Go.
- `Task J`: lokalna walidacja przeciwko realnemu TraceCollectorowi zapisała `9` eventów, a Why API zwróciło chain z `depth >= 2`.

### 2. Najstarszy ślad historyczny

Commit `014d6b7`:

`chore: [M-Platform-01] cleanup and compiler integration updates`

To właśnie ten commit po raz pierwszy dopisuje do `CHANGELOG.md` opisy `Task I` i `Task J`.

Wpisy z tego commita mówią wprost:

- `Task I`: naprawiono lokalną integrację generated Go z TraceCollectorem przez dopasowanie payloadu trace do realnego kontraktu ingest.
- `Task I`: `kind` zmieniono z wartości numerycznej na string event kind.
- `Task I`: zachowano `attributes.narrative_id` w payloadzie `TraceEvent`.
- `Task I`: wynik lokalny to `PASS`, ale chain ma jeszcze głębokość `1`.
- `Task J`: dodano startup trace correlation przez współdzielone `payloadHash`.
- `Task J`: dodano pary `COMM_SEND` i `COMM_RECV` potrzebne do rekonstrukcji przez HB index.
- `Task J`: osiągnięto pierwszy end-to-end path `.jnx -> generated Go -> runtime execution -> TraceCollector -> HBIndex -> Why API`.

### 3. Późniejsze commity porządkujące

- `083e0a3` — rozdziela evidence validation od sekcji `Fixed` w changelogu.
- `a14d981` — dodaje powtarzalny punkt wejścia `scripts/verify-junexis-02.ps1`.
- `f97a627` — utwardza ten skrypt na lock `Release` od aktywnego `JunexisLang.Compiler.LspHost`.

## Pliki z kodem, które niosą ślad Task I / Task J

### Główny generator

`compiler/JunexisLang.Compiler.CodeGen/GoCodeGenerator.cs`

Bieżący stan zawiera już oba elementy:

- helper `_emitTrace(kind string, taskId uint32, narrativeId string, payloadHash string)`
- JSON payload z:
  - `"kind":"%s"`
  - `"attributes":{"narrative_id":"%s","payloadHash":"%s"}`
- startup correlation:
  - `COMM_SEND`
  - `COMM_RECV`
  - `startup-*`

To wskazuje, że aktualny generator reprezentuje stan co najmniej `Task J`, czyli już po rozszerzeniu ponad samo dopasowanie ingest z `Task I`.

### Testy golden

`compiler/JunexisLang.Compiler.CodeGen.Tests/GoldenGoCodeGenTests.cs`

Bieżące asercje również są już stanem po `Task J`:

- oczekują `payloadHash`
- oczekują `COMM_SEND`
- oczekują `COMM_RECV`
- oczekują `"startup-` w wygenerowanym Go

### Changelog

`CHANGELOG.md`

To jest dziś najbardziej wiarygodna, precyzyjna dokumentacja semantycznej różnicy między `Task I` i `Task J`.

## Co udało się potwierdzić w historii

### Potwierdzone

- `Task I` i `Task J` nie są tylko opisem dokumentacyjnym; mają odbicie w generatorze Go i testach golden.
- W `014d6b7` oba zadania zostały scalone w jeden commit razem z innymi zmianami.
- Nie znaleziono osobnych commitów ani branchy zawierających rozdzielone stany `Task I` i `Task J`.
- Reflog także nie pokazuje lokalnego, pośredniego commita pomiędzy stanem sprzed `014d6b7` a samym `014d6b7`.

### Nie da się potwierdzić bez zgadywania

- dokładnego, historycznie zapisanego stanu kodu `Task I only`
- dokładnego momentu, w którym poprawka orkiestracji tasków zwracających prymitywy komunikacyjne została wprowadzona osobno od trace correlation

## Wniosek o odtwarzalności

### Exact restore: NIE

Nie da się odtworzyć dwóch osobnych historycznych stanów kodu `Task I` i `Task J` wyłącznie z dostępnej historii git, bo repo nie zachowało ich jako osobnych commitów ani jako osobnych branchy.

### Nearest recoverable state: TAK

Najbliższy dokładny stan źródłowy to:

- `014d6b7` — pierwszy znany commit zawierający już oba efekty

Najbliższy obecny stan walidowalny to:

- `a14d981` + `f97a627` — wejście i hardening powtarzalnej bramki weryfikacyjnej

## Bezpieczna strategia rekonstrukcji roboczej

Jeżeli celem jest nie tyle odczyt historii, ile zbudowanie dwóch czytelnych commitów roboczych na nowym branchu, to bezpieczna wersja wygląda tak:

1. `Task I` jako commit logiczny:
   - dopasowanie payloadu ingest do TraceCollectora
   - `kind` jako string
   - zachowanie `attributes.narrative_id`
   - brak jeszcze korelacji `payloadHash`
   - brak jeszcze par `COMM_SEND` / `COMM_RECV`
   - oczekiwany rezultat: Why path działa, ale chain depth pozostaje `1`

2. `Task J` jako commit logiczny ponad `Task I`:
   - dodanie `payloadHash`
   - dodanie par `COMM_SEND` / `COMM_RECV`
   - dodanie startup correlation
   - dopięcie orkiestracji tasków zwracających prymitywy komunikacyjne
   - aktualizacja golden tests
   - oczekiwany rezultat: `depth >= 2`

Ta strategia byłaby jednak rekonstrukcją manualną, a nie odtworzeniem exact historical states.

## Status na koniec analizy

- Branch roboczy utworzony: `feat/task-i-j-reconstruction`
- Najbliższy commit źródłowy z evidence: `014d6b7`
- Exact split historyczny: niedostępny
- Manual split logiczny: możliwy, ale wymaga świadomej rekonstrukcji zamiast prostego checkoutu
