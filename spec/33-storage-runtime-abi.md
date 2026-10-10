# M12.3 — Storage Runtime ABI i host referencyjny

> **Status:** VERIFIED BASELINE
>
> **ABI:** `junexis.storage.runtime/0.1`
>
> **Implementacja:** `junexis-runtime/Junexis.Storage.Runtime.*`

## Zakres dowodu

M12.3 ustanawia wykonywalną granicę procesu pomiędzy klientem wygenerowanym
dla dowolnego backendu targetu a providerem danych. Referencyjny host jest
napisany w .NET 10, ale jego wire contract jest językowo neutralnym NDJSON
przez standardowe wejście i wyjście.

```text
Go / C# / Java / Python
    → junexis.storage.runtime/0.1
    → osobny Storage Runtime Host
    → IStorageProviderFactory
    → IStorageProviderSession
    → driver
```

Provider nie jest linkowany do generatora `@server`. Host ładuje jawnie
wybrane assembly zawierające publiczną fabrykę providera. Provider deklaruje
ID, dokładną wersję, resource kind oraz capabilities.

## Koperta

Każdy request zawiera:

- `schemaVersion = "junexis.storage.runtime/0.1"`,
- niepuste `requestId`,
- `action`,
- pola właściwe dla danej akcji,
- opcjonalny dodatni `timeoutMs`.

Każda odpowiedź zachowuje `schemaVersion`, `requestId` i `success`. Błąd
zawiera stabilny kod ABI, klasę `junexis.storage/0.1` i komunikat, który nie
publikuje tekstu obcego wyjątku providera.

## Lifecycle i operacje

Baseline implementuje:

1. `handshake` i negocjację wersji,
2. katalog providerów oraz capabilities,
3. `open` i `close` bindingu,
4. `invoke` operacji z efektem `read`, `write`, `delete` albo `migration`,
5. `begin`, `commit` i `rollback`,
6. `health`,
7. deadline i anulowanie,
8. sekwencyjny wynik przez `cursorId`, `next` i `cancel-sequence`,
9. kontrolowane zamknięcie hosta.

Pojedynczy wynik używa `mode=value`. Duży wynik używa `mode=sequence`; klient
pobiera elementy kursora pojedynczo. Jest to sekwencyjne I/O, a nie
komunikacyjny `stream<T>` języka.

Capabilities nie są wyłącznie opisem. Host odrzuca operację lub rytuał, jeśli
provider nie zadeklarował wymaganej zdolności.

## Bezpieczeństwo konfiguracji

Host otrzymuje rozwiązane wartości konfiguracji dopiero przy `open`, ale ich
nie zwraca. Requesty nie są logowane, a tekst wyjątku providera nie przekracza
granicy. Test lifecycle sprawdza, że wartość sekretu nie występuje w żadnej
odpowiedzi.

M12.2 rozstrzyga pakiet, hash i binding. W baseline M12.3 jawny argument
`--provider` jest zaufanym wejściem technicznym. Launcher `junexis`, który
przed startem porówna entrypoint z `junexis.providers.lock/0.1`, pozostaje
integracją kolejnego pionu. Brak launchera nie może być przedstawiany jako
gotowy publiczny workflow.

## Dowód wykonywalny

Testy w `Junexis.Storage.Runtime.Tests` uruchamiają host również jako osobny
proces i ładują assembly providera kontraktowego. Sprawdzają handshake,
lifecycle bindingu, wywołanie wartości i sekwencji, transakcję, health,
klasyfikację błędu, ochronę sekretu oraz odrzucenie obcego ABI.

Provider kontraktowy nie jest providerem danych. M12.4 przeniesie memory,
M12.5 SQLite, a M12.6 PostgreSQL i native SQL na ustanowioną tutaj granicę.

## Świadome ograniczenia

- requesty są wykonywane sekwencyjnie,
- ABI ma lokalny transport referencyjny, bez zdalnego uwierzytelnienia,
- telemetria i korelacja Narrative nie są jeszcze spięte,
- host nie jest sandboxem dla niezaufanego kodu providera,
- M12.3 nie zmieniało gramatyki; późniejsze M13.2 dodało eksperymentalną
  składnię `storage`, a M13.3 jej neutralny deskryptor `query.plan`.
