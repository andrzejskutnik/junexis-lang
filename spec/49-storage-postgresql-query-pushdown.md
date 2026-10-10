# M13.8 — bezpieczny SQL pushdown Query Plan dla PostgreSQL

> **Status:** VERIFIED SAFE QUERY PUSHDOWN
>
> **Zakres:** Query Plan 0.1, PostgreSQL 17.5, JSONB i parametryzowany SQL
>
> **Poza zakresem:** pełny pushdown typów, joiny, agregacje i mutacje

## Idea

M13.8 nie dodaje SQL do języka Junexis. Ten sam neutralny Query Plan może
zostać wykonany przez wspólny kernel albo obniżony przez provider:

```text
storage/query w .jnx
        │
        ▼
Query Plan 0.1
        │
        ▼
PostgreSQL sprawdza dowiedziony zakres
        │
   ┌────┴─────────────┐
   ▼                  ▼
JSONB + SQL           wspólny kernel
bezpieczny podzbiór   pełny fallback
   └────┬─────────────┘
        ▼
ten sam typowany kursor Runtime ABI
```

Lowerer należy wyłącznie do paczki providera. Kompilator, Host oraz generator
`@server` nie zależą od Npgsql i nie znają fizycznej tabeli.

## Zakres natywny

PostgreSQL obniża kanoniczny ciąg:

```text
filter* → project? → sort? → limit?
```

Pierwszy dowiedziony podzbiór obejmuje:

- równość i nierówność `int64`, `float64`, `bool` oraz `text`;
- porównania porządkowe nienullowalnych `int64`, `float64` i `bool`;
- `and`, `or`, `not` oraz predykat null;
- projekcję wszystkich dziewięciu przenośnych typów;
- stabilny sort z technicznym kluczem w kolacji `C`;
- limit oraz offset.

Provider odczytuje wartości z `jsonb`, wykonuje jawne casty wyłącznie dla
dowiedzionych typów i mapuje wynik z powrotem do kontraktu Runtime ABI.

## Granica bezpieczeństwa

Encja, argumenty, literały, limit i offset są typowanymi parametrami Npgsql.
Nazwy pól i aliasów przechodzą zamkniętą walidację. Wartość użytkownika nie
może stać się fragmentem tekstu SQL.

Jeśli pojedynczy fragment planu nie mieści się w zakresie — na przykład sort
nullable, porządek tekstowy lub niekanoniczna kolejność kroków — cały plan
wraca do kernela referencyjnego. Provider nie miesza dwóch semantyk w jednym
wyniku i nie przybliża zachowania typów.

## Jeden snapshot

Pushdown ma dwie fazy:

1. strumieniowy preflight wszystkich rekordów przez wspólną semantykę wraz z
   budżetami;
2. natywny SQL wykonujący filtr, projekcję, sort i limit.

Obie fazy muszą widzieć jeden stan. Zwykłe wywołanie otrzymuje lokalną
transakcję `RepeatableRead`. Jawne transakcje providera także działają jako
`RepeatableRead`, dzięki czemu pushdown widzi wcześniejszy zapis aplikacji, a
rollback usuwa go bez zmiany wyniku bazowego.

Pierwsza implementacja świadomie wykonuje dwa odczyty. Redukuje materializację
i przenosi pracę relacyjną do serwera, ale osobny walidator rekordów lub
dedykowane kolumny fizyczne pozostają przyszłą optymalizacją.

## Dowód live

23 sierpnia 2026 bramka została wykonana na izolowanej bazie PostgreSQL 17.5:

- 41/41 testów zaliczonych, bez pominięć;
- tekst lowerera zawierał `WHERE`, `ORDER BY`, `LIMIT` i wyłącznie parametry;
- argument przypominający SQL injection pozostał wartością Npgsql;
- rzeczywisty pushdown zwrócił poprawny filtr, projekcję, sort i limit;
- pushdown w jawnej transakcji widział zapis, a po rollbacku wrócił do wyniku
  bazowego;
- natywna projekcja poprawnie zmapowała dziewięć typów;
- plan z nullable sortem pozostał na fallbacku;
- wspólne conformance memory, SQLite i PostgreSQL nadal przeszło.

Losowa baza i rola `junexis_m138_*` zostały usunięte przez `finally`. Kontrola
po teście zwróciła zero pozostałych zasobów. Sekret połączenia nie został
zapisany w repozytorium ani dokumentacji.
