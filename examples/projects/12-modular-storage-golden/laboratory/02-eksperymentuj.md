# Eksperymentuj — wymień pamięć systemu bez zmiany języka

**Czas:** około 60 minut. **Poziom:** samodzielna zmiana. **Providery:** SQLite i memory.

## Pytanie eksperymentu

Czy ten sam system Junexis może użyć innego sposobu przechowywania danych bez
zmiany modułów `.jnx`, kontraktu `HomeStore` i adaptera domenowego?

Nie zakładaj wyniku. Zapisz hipotezę dla dwóch przebiegów:

| Cecha | SQLite | memory |
|---|---|---|
| zapis w bieżącym procesie | ? | ? |
| odczyt w bieżącym procesie | ? | ? |
| odczyt po restarcie | ? | ? |
| zmiana plików `.jnx` | ? | ? |

## Przygotuj dwa warianty

```powershell
$sqlite = ./verification/prepare-m12-runtime-project.ps1 `
  -ProviderPackage <pakiet-sqlite>
$memory = ./verification/prepare-m12-runtime-project.ps1 `
  -ProviderPackage <pakiet-memory>
```

Porównaj wyłącznie:

```powershell
Compare-Object `
  (Get-Content "$sqlite/junexis.project.toml") `
  (Get-Content "$memory/junexis.project.toml")
```

Różnica powinna dotyczyć pakietu, wersji i konfiguracji providera. Katalogi
`src` oraz `user` pozostają takie same.

## Uruchom próbę SQLite

Powtórz zapis i restart z etapu Poznaj. Zanotuj:

```text
provider = sqlite
count po restarcie = ...
restored_after_restart = ...
```

## Uruchom próbę memory

Uruchom drugi wariant tym samym poleceniem, zmieniając tylko katalog projektu:

```powershell
junexis run $memory --duration 120 --no-open `
  --storage-runtime-host <Junexis.Storage.Runtime.Host.dll>
```

Zapisz tę samą obserwację, odczytaj ją, zakończ proces i uruchom projekt ponownie.
W drugim procesie provider memory powinien zwrócić pustą kolekcję oraz
`restored_after_restart: false`.

## Wyjaśnij wynik

Oba providery realizują ten sam kontrakt zapisu, odczytu, health i transakcji.
Nie obiecują jednak tej samej trwałości:

```text
ten sam kod Junexis
        │
        ├── binding sqlite ── trwałość procesu i restartu
        └── binding memory ── stan tylko bieżącego procesu
```

Wymienność oznacza zgodność operacji i błędów, a nie automatyczne zrównanie
wszystkich cech technologii. Dlatego decyzja o providerze jest decyzją
deploymentu, którą trzeba umieć uzasadnić.

## Sprawdź maszynowy ślad decyzji

Po buildzie porównaj pliki:

```powershell
Get-Content "$sqlite/out/debug/junexis.providers.lock.json"
Get-Content "$memory/out/debug/junexis.providers.lock.json"
```

Lockfile ma wskazywać inne ID, capability, entrypoint i SHA-256 pakietu. To
dowód, że wymiana nie jest ukrytą właściwością środowiska.

## Punkt kontrolny

Etap jest ukończony, jeśli potrafisz pokazać ten sam graf modułów dla obu
wariantów, przewidzieć różnicę po restarcie oraz wskazać ją w rezultacie i
lockfile.

[Przejdź do etapu Przejmij kontrolę](03-przejmij-kontrole.md).
