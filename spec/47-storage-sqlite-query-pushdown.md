# M13.6 — bezpieczny SQL pushdown Query Plan dla SQLite

> **Status:** VERIFIED SAFE QUERY PUSHDOWN
>
> **Zakres:** read-only Query Plan 0.1, SQLite, parametryzowany lowering i fallback
>
> **Poza zakresem:** PostgreSQL, agregacje, joiny, mutacje i optymalizator kosztowy

## Idea

Program Junexis nadal opisuje logiczne zapytanie. Nie zawiera SQL i nie zna
fizycznego schematu SQLite. M13.6 zmienia sposób wykonania, a nie znaczenie
języka:

```text
storage/query w .jnx
        │
        ▼
Query Plan 0.1 — wspólny kontrakt
        │
        ▼
SQLite sprawdza, czy potrafi zachować dokładne znaczenie
        │
   ┌────┴─────────────┐
   ▼                  ▼
bezpieczny zakres     zakres niepewny
parametryzowany SQL   wspólny kernel na ograniczonym snapshotcie
   └────┬─────────────┘
        ▼
ten sam typowany kursor Runtime ABI
```

Lowering należy do providera. Kompilator, wygenerowany klient i Runtime Host
nie otrzymały zależności od SQLite.

## Pierwszy zakres natywny

Lowerer przyjmuje kanoniczny ciąg kroków:

```text
filter* → project? → sort? → limit?
```

W M13.6 do SQL trafiają:

- równość i nierówność `int64`, `float64`, `bool` oraz `text`;
- porównania porządkowe nienullowalnych `int64`, `float64` i `bool`;
- logiczne `and`, `or`, `not` i sprawdzenie null;
- projekcja wszystkich dziewięciu przenośnych typów;
- stabilny sort obsługiwanego podzbioru z technicznym kluczem jako tie-breaker;
- limit i offset.

Argumenty, literały, limit, offset oraz encja źródłowa są parametrami
sterownika. Nazwy pól i aliasów muszą przejść zamkniętą walidację
identyfikatora. Dane użytkownika nigdy nie są składane z tekstem SQL.

## Dlaczego istnieje fallback

SQLite i kernel referencyjny nie mają identycznych reguł porządku dla każdego
typu. Przykładowo `null`, tekst, timestamp, UUID, decimal, bytes i kanoniczny
JSON wymagają osobnych dowodów semantycznych. Provider nie zastępuje ich
„podobnym” zachowaniem bazy.

Jeżeli choć jeden fragment planu nie mieści się w dowiedzionym podzbiorze,
SQLite odczytuje ograniczony snapshot i przekazuje cały plan wspólnemu
kernelowi M13.5. Fallback jest poprawną ścieżką wykonania, a nie błędem ani
degradacją kontraktu.

## Walidacja i spójność snapshotu

Pushdown nie może ukryć wadliwego rekordu tylko dlatego, że SQL odfiltrowałby
go przed mapowaniem. Provider najpierw strumieniowo sprawdza wszystkie rekordy
źródła wspólną semantyką i egzekwuje `maxQueryScanRecords` oraz
`maxQueryScanBytes`. Następnie wykonuje obniżony SQL.

Obie fazy korzystają z tej samej transakcji SQLite. Gdy wywołanie nie ma
jawnej transakcji aplikacyjnej, provider tworzy lokalny snapshot tylko na czas
zapytania. Wynik nie może więc połączyć dwóch różnych stanów bazy.

Pierwszy wariant celowo wykonuje dwa odczyty źródła. Ogranicza materializację
i przesuwa filtr, projekcję, sort oraz limit do bazy, ale nie jest jeszcze
ostatecznym modelem wydajności. Kolejny etap może skompilować osobny walidator
rekordu lub mapować encje na dedykowane kolumny bez zmiany Query Plan 0.1.

## Dowód

Bramka M13.6 sprawdza:

- strukturę wygenerowanego SQL dla filtra, projekcji, sortu i limitu;
- parametryzację literałów i niebezpiecznie wyglądającego argumentu tekstowego;
- rzeczywiste wykonanie ścieżki natywnej przez Storage Runtime Host;
- mapowanie natywnej projekcji wszystkich dziewięciu typów przenośnych;
- odmowę pushdown nullable sortu;
- zgodny fallback dla planu z nullable i timestamp;
- trwałe procesowe E2E po ponownym otwarciu pliku SQLite;
- wszystkie wcześniejsze testy providerów i wspólną bramę conformance.

## Następna granica

M13.6 nie oznacza, że każdy Query Plan jest już relacyjnym SQL. Najbliższe
niezależne kierunki to wykonanie Query Plan przez PostgreSQL oraz poszerzanie
pushdown wyłącznie wraz z testami zgodności dla kolejnych typów i operatorów.
