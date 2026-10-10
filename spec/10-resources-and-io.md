# Junexis Lang — Resources, storage i typowane I/O

> **Status:** PARTIAL / EXPERIMENTAL
> **Wersja języka:** kierunek po 0.2
> **Implementacja:** ogólny `resource` ABSENT; relacyjny `storage` ma parser, AST,
> Structural IR i projekcję read-only M13.2; zgodne wykonanie memory/SQLite od M13.5
> oraz bezpieczny SQL pushdown SQLite od M13.6
> **PostgreSQL:** M13.7 zweryfikowane live na PostgreSQL 17.5
> **Pushdown:** bezpieczny podzbiór SQLite M13.6 i PostgreSQL M13.8
> **Decyzja:** D17
> **Data:** 2026-06-23

---

## Cel

System rozproszony nie tylko oblicza i przesyła dane. Czyta je, przechowuje,
archiwizuje, aktualizuje transakcyjnie i odzyskuje po awarii. Bazy danych, pliki,
pamięć oraz magazyny obiektowe są częścią architektury systemu, a nie wyłącznie
szczegółem biblioteki.

Junexis potrzebuje opisać tę odpowiedzialność bez przywiązywania logiki tasku
do PostgreSQL, konkretnego systemu plików albo SDK chmurowego.

> **Task przetwarza dane. Resource udostępnia stan lub I/O. Pipeline przenosi
> dane i pokazuje kierunek ich skutków.**

`resource` jest proponowaną deklaracją logicznej zależności od stanu lub I/O.
Opisuje wymagane możliwości i gwarancje. Konkretny provider, lokalizacja,
poświadczenia i parametry operacyjne należą do deploymentu.

Pierwszym zrealizowanym podzbiorem tej odpowiedzialności jest deklaracja
`storage` z M13.2. Opisuje logiczne encje oraz przenośne zapytania odczytowe i
projektuje je do kontraktów M13.1. Nie zastępuje jeszcze ogólnego `resource`,
bindingu tasku, mutacji ani typowanego I/O. Szczegóły:
[`43-storage-source-projection.md`](43-storage-source-projection.md).
M13.3 obniża sprawdzony plan do operacji `query.plan` istniejącego Storage
Runtime, a M13.4 wykonuje pierwszy pion przez provider memory i typowany kursor;
zob. [`44-storage-runtime-query-lowering.md`](44-storage-runtime-query-lowering.md)
oraz [`45-storage-memory-query-execution.md`](45-storage-memory-query-execution.md).
M13.5 dodaje drugi, trwały backend SQLite i wspólną bramę znaczenia; zob.
[`46-storage-sqlite-query-conformance.md`](46-storage-sqlite-query-conformance.md).
M13.6 obniża dowiedziony podzbiór tego samego planu do parametryzowanego SQL,
zachowując wspólny kernel jako fallback; zob.
[`47-storage-sqlite-query-pushdown.md`](47-storage-sqlite-query-pushdown.md).
M13.7 dodaje ten sam kernel i ograniczony snapshot do providera PostgreSQL;
wspólna bramka została zweryfikowana live na PostgreSQL 17.5. Zob.
[`48-storage-postgresql-query-execution.md`](48-storage-postgresql-query-execution.md).
M13.8 obniża dowiedziony podzbiór planu do parametryzowanego SQL PostgreSQL,
zachowując pełny fallback i jeden snapshot `RepeatableRead`; zob.
[`49-storage-postgresql-query-pushdown.md`](49-storage-postgresql-query-pushdown.md).

## Granice modelu

Resource odpowiada na pytanie:

> Z jakiego stanu albo z jakiej zdolności wejścia/wyjścia korzysta system?

Nie odpowiada samodzielnie na pytania:

- co system robi — to odpowiedzialność `task`,
- dokąd płyną dane — to odpowiedzialność `pipeline`,
- gdzie działa instancja — to odpowiedzialność `deployment`,
- jak przekracza granicę platform — to odpowiedzialność `protocol`,
- jak wywołać obcy kod — to odpowiedzialność `extern`,
- jak wyjaśnić operację — to odpowiedzialność `narrative`.

Resource nie jest także automatycznym singletonem ani globalną zmienną. Musi mieć
określony lifecycle, zakres, właściciela i granicę awarii.

## Trzy warstwy

### 1. Kontrakt logiczny

Kod Junexis powinien deklarować potrzebne możliwości i gwarancje, a nie produkt:

- odczyt i zapis rekordów,
- wyszukiwanie lub skanowanie,
- transakcje,
- sekwencyjny odczyt i zapis,
- losowy dostęp (`seek`),
- obserwowanie zmian,
- retencję i trwałość,
- limit pamięci albo pojemności.

Nazwy możliwości i ich typowanie pozostają otwartą decyzją. Nie należy jeszcze
wprowadzać ich jako słów kluczowych.

### 2. Zależność tasku

Task deklaruje, że potrzebuje zasobu spełniającego kontrakt. Nie powinien znać
produkcyjnego adresu, hasła ani mechanizmu tworzenia połączenia.

Składnia robocza — **pseudokod, obecnie niekompilowalny**:

```junexis
resource orders: Store<Order> {
    require read, write, transaction
}

task ReserveOrder(store: resource orders) @server -> OrderReserved {
    // logika zależy od kontraktu Store<Order>, nie od PostgreSQL
}
```

Możliwe, że finalna składnia użyje parametrów capability zamiast specjalnego
typu `resource`. D17 zamraża odpowiedzialność, nie zapis tekstowy.

### 3. Binding deploymentu

Deployment wybiera provider i jego właściwości operacyjne:

```junexis
// Pseudokod — nie jest częścią gramatyki 0.2.
deployment production {
    bind orders to postgres {
        endpoint: secret("ORDERS_DATABASE_URL")
        pool: 20
    }
}

deployment tests {
    bind orders to memory {
        capacity: 10_000
    }
}
```

Provider pamięciowy może zastąpić bazę tylko wtedy, gdy spełnia gwarancje
wymagane przez task. Kompilator powinien odrzucić binding, który nie zapewnia
na przykład wymaganej trwałości albo transakcyjności.

## Rodziny zasobów

Poniższa klasyfikacja porządkuje projekt. Nie jest listą planowanych keywordów.

| Rodzina | Przykładowe providery | Typowe możliwości |
|---------|-----------------------|-------------------|
| Relational store | PostgreSQL, SQLite | query, transaction, constraint, migration |
| Key-value store | Redis, RocksDB, memory | get, put, compare-and-set, expiry |
| Document store | MongoDB, pliki dokumentów | get, query, replace, watch |
| Object store | S3-compatible, lokalny katalog | open, put, metadata, versioning |
| Filesystem | lokalny FS, volume, flash | open, list, seek, atomic replace |
| Memory | bounded buffer, map, byte arena | read/write, capacity, volatile lifecycle |
| Sequential I/O | plik, socket, device port | source, sink, flush, backpressure |

Junexis nie powinien udawać, że wszystkie rodziny mają wspólny najniższy
mianownik. Relacyjna transakcja, atomowa podmiana pliku i compare-and-set są
różnymi gwarancjami.

## Bazy danych

Minimalny model bazy danych musi określić:

1. **Typ danych** — jaki kontrakt `struct` jest zapisywany lub odczytywany.
2. **Model zapytań** — typowane operacje, zapytanie adaptera albo oba poziomy.
3. **Granice transakcji** — które operacje tworzą jedną niepodzielną decyzję.
4. **Spójność** — jakie odczyty są dopuszczalne po zapisie i między replikami.
5. **Retry i idempotencję** — czy powtórzenie operacji może zmienić wynik.
6. **Migracje** — jak zmiana `struct` wpływa na istniejący stan.
7. **Pooling i lifecycle** — kiedy połączenia powstają, są odnawiane i zamykane.
8. **Sekrety** — poświadczenia nie mogą być zapisane w źródle `.jnx`.
9. **Obserwowalność** — Narrative musi odróżnić próbę, commit, rollback i nieznany wynik.

### SQL

W pierwszych backendach SQL może być używany w adapterze lub kontrolowanej
granicy `extern`. Nie oznacza to, że SQL staje się częścią rdzenia języka.
Przed zaprojektowaniem własnej składni zapytań potrzebny jest dowód, że model
typów, migracji i transakcji pozostaje spójny między backendami.

### Wyniki większe niż pamięć

Zapytanie może zwracać wynik przyrostowo. Taki wynik należy traktować jako
źródło I/O o określonym lifecycle i backpressure, a nie automatycznie jako
rozproszony `stream<T>`. Dopiero jawne opublikowanie wyników tworzy relację
komunikacyjną między taskami.

## Pliki i magazyny obiektowe

Model plików powinien unikać założenia, że każdy backend ma klasyczny system
plików. Task może wymagać logicznych operacji takich jak:

- otwarcie źródła lub ujścia,
- odczyt/zapis sekwencyjny,
- `seek`, jeśli provider go obsługuje,
- listowanie i metadane,
- atomowa podmiana,
- synchronizacja (`flush`/durability),
- usunięcie albo wersjonowanie.

Ścieżka produkcyjna, bucket i mount należą do bindingu. Resource powinien móc
być związany zarówno z lokalnym katalogiem, jak i object storage, jeśli wymagany
kontrakt nie opiera się na niedostępnych operacjach.

## Dwa różne znaczenia „stream”

### Komunikacyjny `stream<T>`

Istniejący `stream<T>` opisuje relację w systemie rozproszonym:

- producentów i subskrybentów,
- ciągłą obserwację,
- możliwość utraty stanów pośrednich zgodnie z przyjętą semantyką,
- fanout, granice platform i transport.

Jest elementem topologii systemu.

### Sekwencyjne I/O

Plik, odpowiedź zapytania, socket albo bufor dostarcza sekwencję bajtów lub
wartości. Roboczo można myśleć o kontraktach `Source<T>` i `Sink<T>`:

Operator `|>` w poniższych przykładach jest wyłącznie wizualnym zapisem
kompozycji. Nie należy do gramatyki 0.2 ani nie jest przyjętą propozycją składni.

```junexis
// Pseudokod — nazwy Source/Sink nie są zamrożone.
let source: Source<Bytes> = open(input)
let events: Source<Event> = source |> gzip.decode |> json_lines.decode<Event>
events |> process |> json_lines.encode |> write(output)
```

Sekwencyjne I/O ma lifecycle uchwytu, błędy odczytu/zapisu, backpressure i
często jednokrotne zużycie. Nie ma samo z siebie subskrybentów ani semantyki
rozproszonego fanout.

Konwersja między warstwami musi być jawna:

- publikacja `Source<T>` do `stream<T>` tworzy producenta komunikatów,
- zapis `stream<T>` do `Sink<T>` tworzy konsumenta i wymaga decyzji o utracie,
  buforowaniu oraz zamknięciu.

## ZIP, GZIP i pamięć

Te pojęcia znajdują się na różnych poziomach:

| Pojęcie | Rola |
|---------|------|
| GZIP | Transformacja kompresji pojedynczego strumienia bajtów. |
| ZIP | Kontener archiwum zawierający nazwane wpisy, opcjonalnie kompresowane. |
| Memory buffer | Ograniczony bufor i strategia przechowywania danych w pamięci. |
| Memory resource | Nietrwały provider logicznego kontraktu storage. |
| File | Provider albo obiekt I/O z lifecycle i operacjami zależnymi od platformy. |

Dlatego nie należy projektować jednego enuma `stream_kind = zip | memory | file`.
Kompozycja powinna zachowywać role:

```junexis
// Pseudokod
resource archive: ObjectStore
resource scratch: Memory { require capacity >= 64.mb }

task Export(archive: resource archive, scratch: resource scratch) @server {
    archive.open_write("events.jsonl.gz")
        |> gzip.encode
        |> json_lines.encode<Event>
        |> write(events)
}
```

Kompilator powinien móc policzyć koszt buforowania i odrzucić konfigurację
niezgodną z budżetem urządzenia.

## Gwarancje, które muszą być jawne

Resource i I/O nie mogą ukrywać kluczowych decyzji operacyjnych:

| Obszar | Pytanie wymagające odpowiedzi |
|--------|-------------------------------|
| Trwałość | Czy potwierdzony zapis przetrwa restart i awarię procesu? |
| Transakcje | Jaki jest zakres atomowości i co oznacza commit? |
| Spójność | Kiedy zapis staje się widoczny dla kolejnego odczytu? |
| Kolejność | Czy kolejność zapisów i odczytów jest zachowana? |
| Idempotencja | Czy bezpiecznie można ponowić operację po timeout? |
| Backpressure | Co dzieje się, gdy odbiorca lub storage jest wolniejszy? |
| Pojemność | Jaki jest limit pamięci, dysku albo liczby otwartych uchwytów? |
| Lifecycle | Kto otwiera, zamyka, odnawia i odzyskuje zasób? |
| Granica awarii | Czy provider znajduje się w procesie, na urządzeniu czy w sieci? |
| Bezpieczeństwo | Kto może czytać, pisać i zarządzać sekretami? |

Nie każda gwarancja musi być parametrem składni. Musi jednak istnieć w kontrakcie
providera, profilu platformy albo deploymentu i być dostępna dla walidacji.

## Platformy

### `@embedded`

- jawne budżety pamięci, flash i liczby uchwytów,
- brak założenia o pełnym filesystemie,
- preferowane bounded buffers i statycznie znane formaty,
- zapis musi uwzględniać zużycie flash oraz przerwanie zasilania,
- zdalna baza oznacza granicę sieci i wymaga protokołu lub bridge tasku.

### `@server`

- pooling, transakcje, migracje i rozproszone providery,
- strumieniowanie dużych wyników bez materializacji w pamięci,
- jawne timeouts, retry i circuit breaking,
- sekrety dostarczane przez deployment.

Lista opisuje typowe potrzeby tasków umieszczonych na `@server`, a nie
własność providerów. D21 oddziela provider i jego runtime od targetu oraz
backendu generatora.

### `@report`

- browser storage, cache i pobieranie plików mają ograniczenia hosta,
- brak bezpośredniego dostępu do produkcyjnej bazy,
- dostęp do zdalnego zasobu zwykle przekracza protocol boundary,
- uprawnienia użytkownika i prywatność są częścią kontraktu.

## Resource, protocol i placement

Jeśli task i provider znajdują się po dwóch stronach granicy wykonania, nie
można udawać lokalnego wywołania bez kosztu. Kompilator powinien:

1. ujawnić granicę w Distributed Topology IR,
2. rozróżnić publiczny protokół aplikacji od technicznego Storage Runtime ABI,
3. określić timeout, retry i idempotencję,
4. instrumentować operację w Narrative,
5. odróżnić błąd providera od błędu transportu.

Resource opisuje potrzebę. Protocol opisuje komunikację logicznych elementów
systemu. Provider Runtime ABI obsługuje techniczną granicę wykonania zasobu.
Deployment decyduje, gdzie ta granica powstaje, i musi zachować ją w analizie
oraz obserwowalności. Szczegóły kierunku M12 opisuje
[`31-independent-storage-runtime.md`](31-independent-storage-runtime.md).

## Narrative

Operacje na zasobach powinny być obserwowalne bez ujawniania sekretów ani
pełnych danych użytkownika. Zdarzenie może zawierać:

- logiczną nazwę resource i rodzaj operacji,
- task i instancję deploymentu,
- rozpoczęcie, powodzenie, rollback albo nieznany wynik,
- czas, rozmiar i liczbę przetworzonych elementów,
- identyfikator transakcji lub korelacji,
- retry oraz przyczynę decyzji,
- zredagowane metadane providera.

Timeout po wysłaniu commitu może oznaczać „wynik nieznany”, a nie „operacja się
nie wydarzyła”. Narrative musi zachować tę różnicę.

## Relacja z `extern`

`extern task` pozostaje potrzebny do adapterów i funkcji specyficznych dla
providera. Nie powinien być jednak jedynym miejscem opisującym zasób, ponieważ
sam podpis FFI nie wyraża trwałości, transakcyjności ani placementu.

W okresie bootstrapowym adapter może wyglądać jak `extern task`, podczas gdy
Semantic IR zachowuje logiczną tożsamość resource i wymagane gwarancje. Dzięki
temu późniejsza zmiana backendu nie zmienia znaczenia programu.

## Proponowana kolejność implementacji

1. Zamrozić minimalny model możliwości i gwarancji, bez katalogu providerów.
2. Ustalić składnię deklaracji zależności tasku oraz bindingu deploymentu.
3. Dodać AST i Semantic IR niezależne od PostgreSQL, filesystemu i SDK chmury.
4. Zaimplementować provider pamięciowy jako szybki model zgodności.
5. Zaimplementować referencyjny provider SQLite i ustalić na nim model
   transakcji, migracji, restartu oraz trwałości potwierdzonego zapisu.
6. Zbudować test E2E podmiany memory ↔ SQLite bez zmiany tasku domenowego.
7. Dodać provider filesystem oraz typowane `Source<T>` / `Sink<T>`, lifecycle i
   backpressure jako osobny pion po kontrakcie storage 0.1.
8. Dodać transformacje bytes/text, kodowanie i GZIP; ZIP jako osobny model archiwum.

Kolejność została zaktualizowana przez M11. SQLite jest wybranym pierwszym
providerem trwałym, ponieważ daje pion migracja → zapis → restart → odczyt bez
zewnętrznego serwera. Nie oznacza to wprowadzenia SQL do rdzenia języka ani
pominięcia kontraktu logicznego resource.

## Kryteria wejścia do wykonywalnego rdzenia

`resource` nie powinien otrzymać statusu CORE ani trafić do gramatyki, dopóki:

- nie zostanie ustalony minimalny kontrakt odpowiedzialności,
- task i deployment nie mają jednoznacznego podziału bindingu,
- model I/O nie koliduje z komunikacyjnym `stream<T>`,
- istnieje reprezentacja w Semantic IR i Distributed Topology IR,
- memory i filesystem przechodzą ten sam zestaw testów zgodności,
- Narrative potrafi opisać sukces, rollback i nieznany wynik,
- istnieje co najmniej jeden pionowy test spec → runtime → E2E.

## Otwarte decyzje

- Ostateczna nazwa `resource` i składnia zależności tasku.
- Czy kontrakty mają używać capabilities, traits/interfaces czy dedykowanych typów.
- Relacja między resource, `state` wewnątrz tasku i `queue<T>`.
- Ownership, borrowing i zamykanie uchwytów I/O.
- Typowanie błędów oraz anulowanie operacji.
- Model transakcji obejmujących więcej niż jeden resource.
- Schemat migracji i kompatybilność wersji `struct`.
- Jak rozszerzyć wykonywany od M14.5 deskryptor `mutation.plan` z memory i
  SQLite na PostgreSQL oraz jak później rozszerzyć DSL o relacje i agregacje
  bez utraty neutralności providera.
- Nazwy robocze `Source<T>` / `Sink<T>` i ich relacja z async execution.
- Semantyka publikacji I/O do `stream<T>` i zapisu strumienia do storage.
