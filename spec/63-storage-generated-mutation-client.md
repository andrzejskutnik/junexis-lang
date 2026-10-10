# M14.9 — generowany klient nazwanych mutacji storage

> **Status:** VERIFIED GENERATED MUTATION E2E
>
> **Target:** `@server` / Go
>
> **Pion:** `.jnx → Mutation Plan → klient Go → Storage Runtime Host → provider`

## Rezultat

Nazwana `mutation` może zostać wywołana przez typowane, provider-neutralne API
Go. Dla deklaracji `HomeData.AddObservation` kompilator emituje:

```text
HomeData_AddObservationParams
HomeData_AddObservationResult
HomeData_AddObservation(ctx, provider, params)
```

Funkcja wiąże wartości uruchomieniowe z kanonicznym Mutation Plan i wykonuje
`mutation.plan/write`. Nie zawiera SQL, nazwy providera, connection stringa ani
sekretów. Provider jest wybierany dopiero przez konfigurację Runtime.

## Artefakt i wynik

`junexis build ... --target server` dodaje plik:

```text
server/junexis/storage/mutations/generated.go
```

Każda operacja otrzymuje własną strukturę parametrów. Typy Storage ABI są
mapowane tak samo jak w kliencie query: `Int` na `int64`, `Float` na `float64`,
`Bool` na `bool`, `Text` i `Uuid` na `string`, `Bytes` na `[]byte`, `Time` na
`time.Time`, `Json` na `json.RawMessage`, a `Decimal` na kanoniczny `string`.
Wartość nullable jest wskaźnikiem.

Jawny wynik nie udaje zwracanego rekordu:

```go
type HomeData_AddObservationResult struct {
    Outcome      string // applied | not-applied
    AffectedRows int64
}
```

Klient waliduje wersję odpowiedzi i spójność `outcome` z `affectedRows`.
`insert single` musi zakończyć się dokładnie jednym zapisem, a każda mutacja
`single` odrzuca wynik większy niż jeden. Błąd pozostaje typowanym
`junexis.storage.Error`, w tym `unknown-outcome`; klient nie wykonuje
automatycznego retry zapisu.

## Dowód Golden

```powershell
./scripts/verify-m14-9.ps1
```

Bramka kompiluje
[`25-storage-generated-mutation-client.jnx`](../examples/grammar/25-storage-generated-mutation-client.jnx),
uruchamia prawdziwy Runtime Host i provider memory, wywołuje wygenerowaną
funkcję `HomeData_AddObservation`, a następnie odczytuje fizyczny rekord przez
neutralny Runtime. Dowodzi to całego pionu, nie tylko obecności pliku Go.

## Granica M14.9

M14.9 udostępnia klienta dla kodu hosta Go, lecz nie dodaje jeszcze składni
wywołania mutation w ciele tasku `.jnx`. Nie obejmuje też grupowania wielu
operacji w transakcję aplikacyjną, idempotency key, polityki retry, raw SQL ani
klientów dla `@embedded` i `@report`. Bezpośrednie związanie named mutation z
taskiem `@server` jest zakresem M14.10.

Ta granica została następnie zrealizowana w
[`64-storage-task-mutation-binding.md`](64-storage-task-mutation-binding.md).
