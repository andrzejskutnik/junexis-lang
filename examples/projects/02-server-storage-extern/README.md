# 02 — Server Storage Extern

Status: **project-first example / RUNNABLE SERVER BUILD**

Ten przykład pokazuje praktyczne użycie nowego blokowego `extern`:

```junexis
extern storage TemperatureStore {
    target server
    language go
    file "user/server/storage.go"

    fn save(reading: TempReading)
    fn recent(limit: Int) -> List<TempReading>
}
```

Znaczenie:

- Junexis opisuje kontrakt storage,
- generator emituje manifest `out/server/externs/junexis.externs.toml`,
- plik `user/server/storage.go` jest własnością developera i zawiera przykładowy memory store,
- `build` dołącza snapshot `user/server/*.go` do `out/server/user/`,
- wygenerowany `main.go` wiąże pasujące `extern task` z pakietem `user`.

Podstawowa praca:

```powershell
dotnet run --project ../../../compiler/JunexisLang.Compiler.CLI -- check
dotnet run --project ../../../compiler/JunexisLang.Compiler.CLI -- build
```

Po buildzie sprawdź:

```text
out/server/externs/junexis.externs.toml
out/server/user/storage.go
```

Serwer Go można sprawdzić z lokalnym cache:

```powershell
cd out/server
$env:GOCACHE = "<workspace-cache>"
go build .
```

`out/` i `.junexis/` są generowane lokalnie i nie powinny być commitowane.
`user/` jest kodem developera i powinien być normalnie wersjonowany w Git.

Ważny szczegół integracyjny: pakiet `user` jest osobnym pakietem Go względem
wygenerowanego `main`. Dlatego scaffold używa `any` dla typów domenowych
Junexis. W czasie wykonania wrapper generatora przekazuje rzeczywistą wartość
`TempReading`, a przy wyniku listowym konwertuje `[]any` z powrotem do
`[]TempReading`.
