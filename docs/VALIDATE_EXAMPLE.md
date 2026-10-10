# validate-example — E2E Validation Framework

Komenda `validate-example` przeprowadza pełną walidację końca do końca dla pliku `.jnx`:

```
check → compile (Go/server) → go build
```

Jeśli dowolny krok się nie powiedzie, komenda kończy się kodem wyjścia innym niż 0.

---

## Użycie

```bash
dotnet run --project compiler/JunexisLang.Compiler.CLI -- validate-example <plik.jnx>
```

**Przykłady:**

```bash
# Walidacja przykładu minimal-pipeline
dotnet run --project compiler/JunexisLang.Compiler.CLI -- validate-example examples/13-minimal-pipeline.jnx

# Wskazanie własnej ścieżki do Go
dotnet run --project compiler/JunexisLang.Compiler.CLI -- validate-example examples/01-temperature-monitor.jnx --go-exe /usr/local/go/bin/go
```

**Opcje:**

| Opcja | Opis | Domyślnie |
|-------|------|-----------|
| `<input>` | Ścieżka do pliku `.jnx` | (wymagane) |
| `--go-exe` | Ścieżka do pliku wykonywalnego `go` | `go` z PATH |

---

## Output

Komenda drukuje postęp do stdout:

```
=== validate-example: 13-minimal-pipeline ===
  [1/3] check ... OK
  [2/3] compile (server) ... OK  → /tmp/junexis_validate_.../server/main.go
  [3/3] go build ... OK
  artifact: /tmp/junexis_validate_.../server
```

W przypadku błędu:

```
=== validate-example: broken-example ===
  [1/3] check ... FAIL
error: E0001 [Parse] unexpected token ...
```

Kod wyjścia:
- `0` — wszystkie kroki zakończyły się sukcesem
- `1` — błąd w dowolnym kroku (check / compile / go build / brak Go w PATH)

---

## Zachowanie specjalne

| Sytuacja | Zachowanie |
|----------|-----------|
| Plik `.jnx` zawiera tylko `@embedded` tasks | `[2/3] compile ... SKIP` — brak `@server` tasks jest oczekiwanym zachowaniem; komenda zwraca 0 |
| Go nie jest zainstalowane | `[3/3] go build ... FAIL` z komunikatem `Go toolchain not found. Install Go from https://go.dev/dl/` |
| Zmienna `JUNEXIS_GO_EXE` | Testy E2E korzystają z tej zmiennej środowiskowej do wskazania ścieżki Go |

---

## Testy E2E

Testy E2E dla tej komendy znajdują się w:

```
compiler/JunexisLang.Compiler.CLI.Tests/ValidateExampleE2ETests.cs
```

Uruchomienie:

```bash
# Wymaga Go w PATH lub zmiennej JUNEXIS_GO_EXE
dotnet test compiler/JunexisLang.Compiler.slnx --filter "Category=E2E"
```

---

## Kandydaci do scenariuszy E2E 0.2

Poniższe przykłady mają testy walidacji E2E, ale ich zielony status nie jest
obecnie traktowany jako pewnik. Muszą zostać ponownie zweryfikowane w
powtarzalnym środowisku wraz z zależnościami Go.

| Przykład | check | compile | go build | Uwagi |
|----------|-------|---------|----------|-------|
| [01-temperature-monitor.jnx](../examples/01-temperature-monitor.jnx) | do weryfikacji | do weryfikacji | do weryfikacji | server + embedded; state persistence |
| [02-ecommerce-api.jnx](../examples/02-ecommerce-api.jnx) | do weryfikacji | do weryfikacji | do weryfikacji | cykliczny pipeline; queue |
| [13-minimal-pipeline.jnx](../examples/13-minimal-pipeline.jnx) | do weryfikacji | do weryfikacji | do weryfikacji | minimalny pipeline; @embedded placeholder |

Docelowo nowe funkcje języka nie powinny być uznawane za `VERIFIED`, dopóki CI
nie potwierdzi pełnego przejścia odpowiednich scenariuszy.

---

## GitHub Actions

Walidacja trzech reprezentatywnych przykładów jest uruchamiana po testach przez
[publiczny workflow CI](../.github/workflows/ci.yml). Workflow przygotowuje .NET
i Go, a następnie wykonuje `validate-example` dla plików wymienionych powyżej.

---

## Golden Tests

Stabilność struktury wygenerowanego kodu Go jest weryfikowana przez golden tests:

```
compiler/JunexisLang.Compiler.CodeGen.Tests/GoldenGoCodeGenTests.cs
```

Testy sprawdzają kluczowe kontrakty (sygnatury funkcji, typy struct, wire-up w `main`) bez porównywania całej zawartości pliku — są odporne na kosmetyczne zmiany codegen.
