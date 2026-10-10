# M14.1 — abstrakcyjny Mutation Plan IR

> **Status:** VERIFIED COMPILER CONTRACT
>
> **Składnia języka:** dodana i projektowana do tego kontraktu w M14.2
>
> **Wykonanie przez Runtime i provider:** poza M14.1

## Idea

M13 zamknął przenośną ścieżkę odczytu. M14 rozpoczyna ścieżkę zapisu, ale nie
przez dopisanie SQL do generatora `@server`. Pierwszy krok ustanawia neutralny
kontrakt intencji:

```text
logiczny model danych
        │
        ▼
Mutation Plan IR
  insert | update | delete
        │
        ├── jawne parametry i przypisania
        ├── jawny zasięg Single albo Many
        └── bezpieczny predykat
                    │
             późniejsze etapy M14
                    ▼
Runtime → memory | SQLite | PostgreSQL | provider zewnętrzny
```

Kompilator opisuje **co ma się zmienić**. Dopiero provider zdecyduje, jak tę
intencję wykonać. Kontrakt nie zawiera SQL, nazw tabel, drivera ani targetowego
języka implementacji.

## Mutation Plan 0.1

`junexis.storage.mutation-plan/0.1` zawiera:

- kanoniczną nazwę modelu i encji;
- nazwę operacji;
- rodzaj `Insert`, `Update` albo `Delete`;
- parametry o typach przenośnego Storage ABI;
- przypisania pola do parametru lub literału;
- predykat dla `Update` i `Delete`;
- jawny zasięg `Single` albo `Many`.

Przykładowa intencja IR — nie jest to jeszcze składnia `.jnx`:

```text
mutation RenameSensor(id: Uuid, sensor: Text)
  update Single Observation
  set sensor = $sensor
  where id == $id
```

Wynik przyszłego wykonania musi co najmniej raportować liczbę zmienionych
rekordów. Dokładny wire result, klasy błędów i obsługa wyniku nieznanego zostaną
zamrożone razem z loweringiem Runtime, a nie pozorowane w M14.1.

## Bezpieczeństwo przez konstrukcję

Walidator nie pozwala providerowi zgadywać zamiaru autora:

| Reguła | Znaczenie |
|---|---|
| `Insert` jest zawsze `Single` | jedna deklaracja tworzy jeden rekord |
| `Insert` nie ma predykatu | nie miesza tworzenia z wyborem istniejących danych |
| wszystkie pola non-nullable są przypisane | brak rekordu częściowo poprawnego |
| `Update` nie zmienia klucza | tożsamość rekordu pozostaje stabilna |
| `Update` i `Delete` wymagają predykatu | brak przypadkowego zapisu całej encji |
| `Single` wiąże pełny klucz przez `==` | zasięg jednego rekordu jest dowiedziony statycznie |
| parametr klucza `Single` nie może być nullable | `null` nie udaje identyfikatora rekordu |
| `Many` jest jawne | zapis masowy nie powstaje przez pominięcie zabezpieczenia |
| przypisania przyjmują parametr albo literał | brak ukrytej, zależnej od providera ewaluacji |

Plan nie oznacza automatycznie, że operację można bezpiecznie ponowić. Retry,
idempotency key i rozstrzygnięcie wyniku nieznanego wymagają osobnego kontraktu.
Domyślnym znaczeniem M14.1 jest **brak automatycznego retry**.

## Walidacja

Kontrolowane problemy `SMPxxx` obejmują:

- wersję planu i zgodność modelu;
- nieznaną encję oraz zduplikowane parametry;
- niedozwolony kształt `Insert`, `Update` lub `Delete`;
- brakujące, nieznane albo wielokrotnie przypisane pole;
- próbę zmiany klucza;
- niezgodność typu lub nullability;
- nieznane pole i parametr predykatu;
- niedowiedziony pełny klucz dla `Single`.

Walidacja logicznego modelu nadal korzysta ze wspólnego kontraktu
`junexis.storage.model/0.1` i jego problemów `SQPxxx`.

## Trzy poziomy pracy

### Początkujący

Docelowe narzędzia powinny wybierać `Single`, generować predykat pełnego klucza
i nie proponować zapisu masowego jako łatwego skrótu. Zwykłe Build nie może
zmienić danych.

### Średniozaawansowany

Developer widzi zasięg operacji, typy przypisań i przyczynę odrzucenia planu.
`Many` wymaga świadomego wyboru i pozostaje widoczne w analizie artefaktu.

### Ekspert

Ekspert może projektować operacje masowe i późniejsze polityki transakcji oraz
idempotencji, ale nie omija kontraktu typów ani jawnego zasięgu mutacji.

## Granica M14.1

Sam kontrakt M14.1 nie dodawał jeszcze:

- składni mutacji do deklaracji `storage`;
- projekcji AST i lokalizacji diagnostyk `.jnx`;
- loweringu do Storage Runtime ABI;
- wykonania przez memory, SQLite lub PostgreSQL;
- generowanego klienta i wywołania z tasku;
- transakcji wielooperacyjnych, upsertu, raw SQL ani automatycznego retry.

To celowa granica: najpierw wspólne znaczenie i bezpieczeństwo, potem transport
i fizyczne wykonanie. M14.2 zrealizował składnię i projekcję źródłową opisaną w
[`56-storage-mutation-source-projection.md`](56-storage-mutation-source-projection.md),
ale nie zmienił granicy Runtime i providera.
M14.3 zrealizował neutralny lowering Runtime opisany w
[`57-storage-runtime-mutation-lowering.md`](57-storage-runtime-mutation-lowering.md),
ale nadal nie dodał fizycznego wykonania przez Host ani provider.
M14.4 dodał pierwsze takie wykonanie przez memory; trwałe providery i binding z
tasku nadal pozostają osobnymi etapami.
M14.5 potwierdził ten sam plan w trwałym SQLite; PostgreSQL i binding z tasku
pozostają kolejnymi granicami.

## Bramka odbioru

```powershell
./scripts/verify-m14-1.ps1
```

Bramka buduje całe rozwiązanie i uruchamia pełny pakiet testów semantycznych.
Testy M14.1 sprawdzają poprawny insert, update po kluczu złożonym, jawny zapis
masowy, pola wymagane, nullability, ochronę klucza, wersje, encję i typy
predykatu.
