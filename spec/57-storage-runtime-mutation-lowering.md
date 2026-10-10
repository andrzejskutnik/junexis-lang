# M14.3 — Mutation Plan w Storage Runtime ABI

> **Status:** VERIFIED COMPILER CONTRACT
>
> **Zakres:** lowering, kanoniczne wire input, fingerprint i kontrakt wyniku
>
> **Wykonanie przez provider:** jeszcze nie; zaczyna się w M14.4

## Cel

M14.2 pozwala opisać zapis w `.jnx`. M14.3 przenosi tę intencję przez istniejącą
granicę Storage Runtime bez generowania SQL i bez wyboru bazy:

```text
mutation w .jnx
    → Mutation Plan IR
    → mutation.plan / write
    → junexis.storage.mutation-invocation/0.1
    → późniejszy provider
```

To odpowiednik M13.3 dla ścieżki zapisu.

## Deskryptor wywołania

| Pole | Wartość M14.3 |
|---|---|
| Runtime ABI | `junexis.storage.runtime/0.1` |
| action | `invoke` |
| operation | `mutation.plan` |
| effect | `write` |
| required capability | `mutation-plan` |
| input | `junexis.storage.mutation-invocation/0.1` |

Wejście zawiera logiczny model, Mutation Plan, encję, rodzaj i zasięg,
uporządkowane parametry i przypisania, opcjonalny predykat oraz argumenty.

```json
{
  "runtimeAbi": "junexis.storage.runtime/0.1",
  "action": "invoke",
  "operation": "mutation.plan",
  "effect": "write",
  "requiredCapabilities": ["mutation-plan"],
  "input": {
    "schemaVersion": "junexis.storage.mutation-invocation/0.1",
    "mutation": {
      "schemaVersion": "junexis.storage.mutation-plan/0.1",
      "name": "RenameSensor",
      "model": "HomeData",
      "entity": "Observation",
      "kind": "update",
      "cardinality": "single",
      "assignments": [
        { "field": "sensor", "value": { "kind": "parameter", "name": "sensor" } }
      ]
    }
  }
}
```

Pełny dokument zawiera również model, parametry, argumenty i predykat.

## Kanoniczne wartości

Query i Mutation korzystają z jednego kodeka dziewięciu typów Storage ABI:
`int64`, `float64`, `bool`, `text`, `bytes`, `decimal`, `timestamp`, `uuid` i
`json`. UUID ma lowercase i myślniki, timestamp zachowuje zapis round-trip,
decimal usuwa nieznaczące zera, a JSON jest serializowany kanonicznie. Jawny
argument null pozostaje obecny w JSON jako `"value": null`.

Kolejność dostarczenia argumentów nie wpływa na JSON ani SHA-256 deskryptora.
Fingerprint nie obejmuje bindingu, sekretu, deadline ani wybranego providera.

## Jawny wynik zapisu

Pomyślny provider zwraca `junexis.storage.mutation-result/0.1`:

```json
{
  "schemaVersion": "junexis.storage.mutation-result/0.1",
  "outcome": "applied",
  "affectedRows": 1
}
```

| outcome | affectedRows | Znaczenie |
|---|---:|---|
| `applied` | `> 0` | skutek został potwierdzony |
| `not-applied` | `0` | operacja była poprawna, ale niczego nie zmieniła |

`Single` nie może zwrócić więcej niż jednego rekordu, pomyślny `Insert` zawsze
zwraca `applied` i dokładnie `1`, a liczba rekordów nie może być ujemna.

### Wynik nieznany nie jest sukcesem

Jeżeli po wysłaniu zapisu Runtime nie potrafi stwierdzić, czy skutek został
utrwalony, provider zwraca istniejącą klasę błędu ABI `unknown-outcome`.
Nie wolno zwrócić sukcesu z `outcome: "unknown"`, zera ani zgadywanej liczby.

```text
applied / not-applied  → kanał sukcesu mutation-result
unknown-outcome        → kanał błędu Storage Runtime
```

To rozdzielenie jest konieczne przed projektowaniem retry i idempotency key.

## Walidacja loweringu

Problemy `SMRxxx` obejmują brakujący, powtórzony albo obcy argument, niezgodny
typ, nullability lub wartość kanoniczną, niepoprawny element wire, niespójny
wynik, naruszenie `Single` oraz próbę przedstawienia `unknown-outcome` jako
sukcesu. Niepoprawny Mutation Plan jest zatrzymywany wcześniej z jego
pierwotnym problemem `SMPxxx`.

## Granica M14.3

M14.3 nie zmienia jeszcze Runtime ani providerów. Nie dodaje capability do
manifestów, obsługi `mutation.plan` w Host, wykonania przez memory, SQLite lub
PostgreSQL, transakcji wielooperacyjnych, automatycznego retry, idempotency key
ani wywołania mutation bezpośrednio z tasku.

M14.4 zrealizował granicę Host i pierwsze wykonanie przez provider memory,
opisane w
[`58-storage-memory-mutation-execution.md`](58-storage-memory-mutation-execution.md).
SQLite, PostgreSQL oraz wywołanie z tasku pozostają poza M14.3–M14.4.

## Bramka odbioru

```powershell
./scripts/verify-m14-3.ps1
```

Bramka buduje rozwiązanie oraz uruchamia pełne testy Core. Testy M14.3
potwierdzają lowering źródłowej mutacji, brak SQL i providera, deterministyczny
fingerprint, wspólną kanonizację typów, walidację argumentów oraz kontrakt
wyniku zapisu.
