# Junexis Lang — moduły i referencyjny provider SQLite

> **Kamień milowy:** M11
> **Status:** VERIFIED — M11.1–M11.10
> **Zakres:** resolver modułów `.jnx`, biblioteki lokalne i pierwszy pion storage
> **Data decyzji:** 2026-08-13

## Cel

Projekt Junexis ma składać się z wielu plików i bibliotek bez kopiowania kodu.
Ten sam mechanizm ma umożliwić dostarczenie pierwszego referencyjnego providera
trwałego stanu: SQLite dla zadań `@server`.

> **Kontrakt M11:** jeden projekt importuje typy, funkcje i zadania z innych
> plików `.jnx`, wiąże logiczny kontrakt storage z SQLite i po restarcie
> odczytuje wcześniej zatwierdzony zapis.

M11 łączy dwa zależne problemy, ale ich nie miesza:

- moduł organizuje kod Junexis,
- resource opisuje wymaganą zdolność i gwarancje,
- provider SQLite realizuje tę zdolność na `@server`,
- deployment wybiera konfigurację providera,
- `extern` pozostaje granicą implementacji natywnego sterownika.

## Moduły nie są tekstowym `include`

Import ładuje deklaracje i rozwiązuje symbole. Nie wkleja tekstu pliku przed
parsowaniem. Dzięki temu każdy moduł jest parsowany raz, zachowuje własne
źródło diagnostyki i może być analizowany jako element grafu zależności.

Istniejące formy składni pozostają punktem wyjścia:

```junexis
import home::domain
import home::persistence as persistence
import home::reports { HomeObservation, BuildHomeStatus as BuildStatus }
```

Znaczenie:

| Forma | Widoczność |
|---|---|
| `import a::b` | publiczne symbole modułu w bieżącym zakresie |
| `import a::b as x` | symbole dostępne przez przestrzeń `x` |
| `import a::b { X, Y }` | tylko wskazane symbole |
| `import a::b { X as Z }` | wskazany symbol pod lokalnym aliasem |

Tekstowe `include` może nadal istnieć wewnątrz adaptera C++ jako detal natywnej
biblioteki. Nie jest mechanizmem kompozycji kodu `.jnx`.

## Jednostka i układ projektu

Jednostką budowania pozostaje projekt z `junexis.project.toml`:

```text
home-system/
├── junexis.project.toml
├── src/
│   ├── main.jnx
│   ├── domain/
│   │   ├── observations.jnx
│   │   └── decisions.jnx
│   ├── persistence/
│   │   ├── contract.jnx
│   │   └── home_store.jnx
│   └── reports/
│       └── home_report.jnx
└── migrations/
    └── 001-initial.sql
```

Kanoniczne mapowanie pierwszej wersji:

```text
src/domain/observations.jnx → domain::observations
```

Ścieżka modułu jest względna wobec deklarowanego katalogu źródeł. Resolver:

- nie wychodzi poza dozwolone katalogi projektu i zależności,
- odrzuca niejednoznaczne dopasowania,
- porównuje tożsamość ścieżek zgodnie z regułą przenośną między systemami,
- nie zależy od bieżącego katalogu procesu,
- zachowuje deterministyczną kolejność grafu.

## Co może eksportować moduł

Pierwszy pełny kontrakt obejmuje:

- `struct`, `enum`, aliasy i inne obsługiwane typy,
- czyste `fn`,
- `task` oraz jego sygnaturę komunikacyjną,
- deklaracje `extern`,
- logiczne kontrakty resource,
- potoki używane przez moduł wejściowy.

Pipeline i deployment systemu powinny mieć jednego jawnego właściciela w
module wejściowym. Importowana biblioteka nie może po cichu uruchamiać tasków
ani zmieniać deploymentu projektu.

## Graf i diagnostyka

Kompilator buduje graf modułów przed pełną analizą semantyczną. Musi wykryć:

- brak modułu,
- brak importowanego symbolu,
- kolizję nazw,
- alias zasłaniający inną deklarację,
- cykl importów,
- dwa pliki deklarujące tę samą tożsamość modułu,
- zależność wychodzącą poza projekt,
- różnicę wielkości liter prowadzącą do nieprzenośnego projektu.

Pierwsza wersja odrzuca wszystkie cykle importów z pełną ścieżką cyklu. Możliwe
późniejsze dopuszczenie cykli samych typów wymaga osobnej decyzji semantycznej.

Diagnostyka i LSP wskazują oryginalny plik, wiersz i kolumnę. `definition`,
`references`, completion i rename muszą działać przez granicę modułu.

## Biblioteki

M11 ma trzy poziomy kompozycji:

1. moduły tego samego projektu,
2. lokalne biblioteki wskazane w manifeście,
3. wersjonowane pakiety z plikiem blokady — po ustaleniu formatu pakietu.

Pierwszy pion musi w pełni domknąć poziomy 1–2. Zdalny rejestr pakietów nie jest
warunkiem M11, ale graf i manifest nie mogą zamknąć drogi do wersjonowania,
sum kontrolnych i offline restore.

## Dlaczego SQLite jest resource, a nie protocol

SQLite działa w procesie serwera i udostępnia trwały stan. Nie jest logicznym
protokołem komunikacji pomiędzy taskami. Dlatego:

- resource opisuje potrzebę zapisu, odczytu i transakcji,
- provider SQLite implementuje te gwarancje,
- `extern` wiąże wygenerowany kod z natywnym sterownikiem,
- protocol pojawia się dopiero wtedy, gdy dostęp przekracza granicę procesu lub
  środowiska.

`@report` nie otwiera pliku SQLite. Panel wysyła typowaną intencję do
`@server`, a serwer wykonuje autoryzowaną operację na zasobie.

### Uproszczony wzorzec modelu storage

Dla scenariuszy dydaktycznych i dokumentacji deweloperskiej przepływ związany
wyłącznie z bazą można przedstawiać następująco:

```text
@embedded ── dane ──> @server ── zapis/odczyt ──> SQLite
                         │                         │
                         └──── projekcja <────────┘
                                   │
                                   v
                               @report
```

Wzorzec oznacza, że `@server` jest właścicielem dostępu do SQLite. `@embedded`
dostarcza dane, a `@report` otrzymuje typowaną projekcję. Intencja użytkownika
z `@report` wraca do zadania `@server`; panel nie modyfikuje bazy bezpośrednio.

Jest to celowe uproszczenie przepływu storage, a nie ograniczenie pełnych ról
platform. `@embedded` może również podejmować lokalne decyzje i sterować
urządzeniami, a `@report` inicjować działania. W zweryfikowanym pionie M11
serwerowy SQLite pozostaje jednak dostępny wyłącznie z `@server`.

## Kontrakt logiczny storage

M11 zamraża semantykę przed ostatecznym zamrożeniem składni. Minimalny resource
relacyjny musi opisać:

- lifecycle otwarcia i zamknięcia,
- typowane operacje domenowe,
- parametryzowane wartości zapytań,
- granicę transakcji,
- commit, rollback i wynik nieznany,
- timeout i anulowanie,
- trwałość potwierdzonego zapisu,
- migracje schematu,
- klasyfikację błędów,
- health check,
- ślad Narrative bez treści sekretów i pełnych rekordów.

Roboczy przykład znaczenia — nie jest jeszcze zamrożoną składnią:

```junexis
resource HomeObservations: RelationalStore<HomeObservation> {
    require read, write, transaction, migration
}

task SaveObservation(
    store: resource HomeObservations,
    observation: HomeObservation
) @server -> SaveResult
```

Binding środowiska:

```junexis
deployment LocalHome {
    bind HomeObservations to sqlite {
        path: data("home.db")
        migrations: "migrations"
    }
}
```

Powyższy zapis jest projektem kontraktu D17. Do gramatyki trafi dopiero razem z
AST, Semantic IR, walidacją deploymentu i wykonywalnym testem.

## Okres bootstrapowy `extern storage`

Istniejąca deklaracja może dostarczyć pierwszy sterownik przed finalną składnią
resource:

```junexis
extern storage HomeStore {
    target server
    language go
    file "user/server/home_store.go"

    fn save(observation: HomeObservation) -> SaveResult
    fn latest(room: Text, limit: Int) -> [HomeObservation]
}
```

Kompilator powinien generować operacje adaptera bez wymagania powtórzenia każdej
z nich jako osobnego `extern task`. Semantic IR zachowuje, że jest to efekt
storage, a nie czysta funkcja.

Bootstrap jest etapem migracji, nie docelowym utożsamieniem resource z FFI.

## Referencyjny provider SQLite

Pierwszy provider:

- działa na Windows x64 bez wymogu instalowania kompilatora C,
- tworzy bazę dopiero podczas uruchomienia, nigdy podczas `check` lub analizy,
- używa parametryzowanych zapytań,
- stosuje migracje dokładnie raz i zapisuje ich wersję,
- nie zmienia istniejącej migracji po jej zastosowaniu,
- stosuje transakcję dla migracji oraz wielooperacyjnych zapisów,
- ma ograniczony czas oczekiwania na blokadę,
- kontrolowanie zamyka połączenia,
- zapisuje metryki operacji bez danych wrażliwych,
- rozróżnia błąd, rollback i nieznany wynik po przerwaniu.

Wybór konkretnej biblioteki Go zostanie zapisany w ADR. Referencyjny sterownik
powinien unikać CGO, aby zachować przenośne paczki i instalację początkującego.

## Trzy poziomy użytkownika

### Początkujący

- `junexis init` może dodać lokalną bazę bez ręcznego kodu Go,
- plik trafia do kontrolowanego katalogu danych projektu,
- bezpieczne timeouty, migracje i zamknięcie są domyślne,
- build nie usuwa ani nie nadpisuje danych,
- reset danych wymaga jawnego polecenia i dokładnego celu.

### Średniozaawansowany

- wybiera migracje i typowane operacje domenowe,
- konfiguruje limit połączeń, timeout i retencję,
- widzi plan bindingu oraz diagnostykę blokady,
- testuje provider pamięciowy i SQLite tym samym kontraktem.

### Ekspert

- kontroluje ścieżkę, tryb dziennika, synchronizację i politykę migracji,
- może dostarczyć własny provider,
- otrzymuje pełny manifest gwarancji i ograniczeń,
- sam odpowiada za zgodność ustawień niestandardowych z deploymentem.

Narzędzia nie zmieniają po cichu ustawień eksperta.

## Bieżący rytm pracy dewelopera

Nowy projekt wieloplikowy powinien być rozwijany project-first:

```text
junexis.project.toml
    → sourceRoots i opcjonalne localLibraries
    → graf importów
    → kontrakt extern storage
    → adapter autora w user/server
    → build i manifesty kontrolne
    → uruchomienie memory albo SQLite
    → test ponownego uruchomienia
```

Zalecana kolejność:

1. rozdziel typy domenowe, kontrakt storage, zadania aplikacyjne i panel na
   osobne moduły,
2. pozostaw pipeline, protocol i deployment w module wejściowym,
3. używaj selective import albo aliasu w obszarach podatnych na kolizje,
4. zadeklaruj operacje w `extern storage`,
5. w okresie bootstrapowym dodaj odpowiadające `extern task`, ponieważ to one
   są jeszcze bezpośrednio wywoływane z tasków Junexis,
6. implementuj mapowanie domenowe w zadeklarowanym pliku `user/server/*.go`,
7. uruchom `doctor`, `check`, `analyze` i `build`,
8. skontroluj `junexis.modules.lock.json`, `junexis.externs.toml`,
   `junexis.storage.contracts.json`, snapshot migracji i adaptera,
9. porównaj provider memory i SQLite tym samym testem operacji,
10. sprawdź trwałość przez zakończenie pierwszego procesu i uruchomienie
    drugiego na tym samym pliku bazy.

`junexis extern scaffold` nie nadpisuje kodu autora. Aktualnie analizuje jednak
moduł wejściowy, a nie cały osiągalny graf importów. Jeśli `extern storage`
znajduje się w zalecanym osobnym module, deweloper tworzy plik wskazany przez
`file` jawnie. Rozszerzenie scaffoldingu na importowane moduły oraz usunięcie
powtórzonych `extern task` należą do następnego etapu automatycznego bindingu.

Nazwy zmiennych środowiskowych wybierających provider i ścieżkę bazy są dziś
polityką adaptera autora, nie standardem języka. Konfiguracja nie może trafiać
do bundle `@report`, a `check` i build nie mogą otwierać bazy ani stosować
migracji.

### Aktualizacja po M13.11: moduł z deklaracją `storage`

Dla nowego, abstrakcyjnego Query Plan developer nie musi już powtarzać
`extern task`, aby odczytać dane z modelu utrzymywanego w osobnym pliku.
Kanoniczny wariant projektu to:

```junexis
import data::home { HomeData }

task ReadHome(homeId: Uuid) @server -> List<HomeData_ByHomeRow> {
    return HomeData_ByHome(homeId: homeId)
}
```

`HomeData` pochodzi z importowanego modułu, a funkcję i typ wiersza syntetyzuje
kompilator. Starszy workflow `extern storage` pozostaje właściwy dla ręcznych,
natywnych adapterów M11; nie jest wymagany dla wykonywalnych named query
M13.11. Szczegóły opisuje
[`52-imported-storage-query-binding.md`](52-imported-storage-query-binding.md).

Od M13.12 selective import może nadać modelowi lokalny alias:

```junexis
import data::home { HomeData as Store }
```

Kod bieżącego modułu używa wtedy `Store_Query` i `Store_QueryRow`, natomiast
wspólny IR, Runtime oraz provider nadal identyfikują model jako `HomeData`.
Alias nie jest nazwą schematu ani bindingu deploymentu. Kontrakt opisuje
[`53-storage-import-alias.md`](53-storage-import-alias.md).

## Etapy M11

| Etap | Rezultat | Dowód |
|---|---|---|
| M11.1 ✅ | kanoniczna tożsamość modułu i source roots | deterministyczne mapowanie, granice projektu, kolizje i integracja CLI |
| M11.2 ✅ | resolver i graf importów | glob, alias, selective, brak modułu, różnica wielkości liter i cykl |
| M11.3 ✅ | wspólna tablica symboli | typy, `fn`, `task`, `extern` przez wiele plików |
| M11.4 ✅ | scalony Semantic IR i codegen | osiągalne moduły tworzą jeden program dla IR, topologii i backendów |
| M11.5 ✅ | workspace LSP | definition, references, rename i diagnostyka między plikami |
| M11.6 ✅ | lokalna biblioteka + manifest | namespaced path dependency i deterministyczny manifest SHA-256 |
| M11.7 ✅ | `junexis.storage/0.1` | lifecycle, efekt, błąd, transakcja i gwarancje |
| M11.8 ✅ | provider SQLite | migracja, zapis, odczyt, commit, rollback i ponowne otwarcie |
| M11.9 ✅ | provider pamięciowy zgodności | te same operacje domenowe bez trwałości |
| M11.10 ✅ | Golden Modular Storage E2E | wiele `.jnx` → SQLite → restart → odczyt → panel i System Bundle |

M11.1 jest zweryfikowany testami rdzenia i CLI. Manifest przyjmuje
`sourceRoots`, zachowuje zgodność przez domyślne `["src"]`, a współdzielony
katalog modułów wyprowadza przenośne tożsamości i stabilną kolejność niezależną
od kolejności enumeracji systemu plików. M11.2 parsuje każdy moduł raz, zachowuje
postać importu glob, alias i selective na krawędzi grafu oraz wyprowadza
deterministyczną kolejność dependency-first. `junexis check` i `junexis build`
zatrzymują projekt przy braku modułu, nieprzenośnej pisowni albo cyklu i
wskazują oryginalny importowany plik przy błędzie parsera. M11.3 buduje osobny
zakres symboli dla każdego modułu, obejmujący typy, `fn`, `task`, `extern task`,
`extern type` i kontrakty `extern`. Glob, alias modułu, selective i alias
symbolu wpływają na widoczność semantyczną; brak symbolu daje `E0007`, a
kolizja `E0002`. Semantyka sprawdza każdy moduł z jego własnym zakresem.
Scalenie deklaracji w Semantic IR, topologii i codegenie zostało wykonane w
M11.4. Etap komponuje osiągalne moduły w kolejności dependency-first i
przekazuje jeden program do Structural IR, topologii oraz generatorów.
Importowane typy, funkcje, taski i `extern` zachowują własne położenie źródłowe.
Pipeline, deployment i pozostałe deklaracje uruchamiające system pochodzą
wyłącznie z modułu wejściowego; biblioteka nie może aktywować ich samym
importem. Test CLI dowodzi, że pipeline wejściowy uruchamia importowany task
`@server`, a jego implementacja trafia do wygenerowanego Go.

M11.5 indeksuje wszystkie moduły z manifestowych `sourceRoots`, również gdy
nie zostały otwarte w edytorze. Go to Definition przechodzi do innego pliku,
references i rename obejmują cały workspace, a diagnostyka semantyczna używa
tego samego grafu oraz zakresów symboli co CLI. Bufor otwartego dokumentu
pozostaje nadrzędny wobec treści odczytanej z dysku.

M11.6 dodaje `localLibraries = ["namespace=path"]`. Jawny namespace izoluje
tożsamości biblioteki, a ścieżka pozostaje zależnością lokalną względem projektu.
Build obejmuje bibliotekę tym samym resolverem i publikuje
`junexis.modules.lock/0.1` z uporządkowanymi modułami, ścieżkami i SHA-256.
Powtórny build niezmienionych źródeł daje identyczny manifest. Zdalne wersje,
registry i restore pozostają świadomie poza M11.6.

M11.7 zamraża niezależny od technologii kontrakt `junexis.storage/0.1` opisany
w [`25-storage-contract.md`](25-storage-contract.md). Semantic IR rozróżnia
rodzaj operacji oraz jej efekt, a generator serwera zapisuje wersjonowany
`server/storage/junexis.storage.contracts.json`. Manifest obejmuje lifecycle,
wynik operacji, stabilne klasy błędów, timeout i anulowanie, granice oraz wyniki
transakcji i wymagane gwarancje. Istniejące `extern storage` jest warstwą
bootstrapową; SQLite nie jest jeszcze uznane za zgodny provider przed M11.8.

M11.8 dostarcza opisany w [`26-sqlite-provider.md`](26-sqlite-provider.md)
referencyjny pakiet Go bez CGO. Generator dołącza przypięty driver, lifecycle,
health check, parametryzowane `Exec` i `Query`, jawną transakcję oraz migracje z
SHA-256. Build projektu kopiuje uporządkowane pliki z `migrations/` do snapshotu
serwera. Natywny test dowodzi migracji, zapisu, commitu, rollbacku, odczytu po
ponownym otwarciu, odporności wartości na SQL injection i odrzucenia zmienionej
zastosowanej migracji przy `CGO_ENABLED=0`. Restart całego procesu pozostaje
dowodem Golden E2E w M11.10.

M11.9 wydziela wspólną granicę wykonawczą `storage.Store` i implementuje ją w
providerach SQLite oraz memory. Ten sam test natywny sprawdza lifecycle,
defensywne kopie, zapis, odczyt, deterministyczną listę, commit, rollback,
delete, `not-found` i anulowanie. Provider pamięciowy stosuje izolowaną kopię
transakcji i wykrywa konflikt wersji, ale jawnie nie oferuje trwałości ani
migracji. Szczegóły opisuje
[`27-memory-storage-provider.md`](27-memory-storage-provider.md).

M11.10 realizuje opisany w
[`28-golden-modular-storage-e2e.md`](28-golden-modular-storage-e2e.md) projekt
referencyjny z pięciu modułów. Zapis przez zadanie serwera trafia do SQLite,
kontrolowany rollback nie pozostawia rekordu, a drugi proces odczytuje
zatwierdzoną obserwację przez API. Panel otrzymuje `HomeSnapshot` wyłącznie z
zadania `@server`. Próba ujawniła i domknęła rozjazd, przez który `analyze` oraz
bramka artefaktów nie komponowały importowanych modułów. System Bundle 0.1
został następnie zweryfikowany i uruchomiony z katalogu tymczasowego: natywny
serwer Windows x64 zapisał rekord do SQLite, drugi proces odtworzył go bez
checkoutu oraz toolchainu Go/C, a samodzielny runtime raportu udostępnił panel i
zbudowany bundle przeglądarkowy. Dowód zachowuje rzeczywiste Build ID i Bundle
ID. Paczka pozostaje niepodpisana; polityka permissive zaakceptowała integralny
bundle, a podpis wydania nie jest kryterium M11.

## Kryteria odbioru

M11 jest ukończony wyłącznie wtedy, gdy:

1. projekt buduje co najmniej cztery współpracujące moduły `.jnx`,
2. importy glob, alias i selective mają testy semantyczne,
3. kolizja, brak symbolu i cykl dają stabilną diagnostykę ze ścieżką,
4. źródło błędu wskazuje właściwy importowany plik,
5. zadanie z importowanego modułu trafia do właściwego targetu i topologii,
6. LSP przechodzi do definicji w innym pliku,
7. ten sam kontrakt domenowy działa z providerem pamięciowym i SQLite,
8. migracja tworzy schemat, a zmieniona zastosowana migracja jest odrzucana,
9. zatwierdzony rekord jest odczytywany po restarcie procesu,
10. rollback nie pozostawia częściowego skutku,
11. parametry zapytania nie są składane przez konkatenację tekstu,
12. `@report` korzysta z danych przez zadanie serwera, nie przez plik bazy,
13. pakiet Windows x64 uruchamia SQLite bez checkoutu i bez toolchainu C,
14. Build ID, Bundle ID, moduły, migracje i provider są zapisane w dowodzie E2E.

Samo parsowanie `import`, obecność deklaracji `extern storage` albo ręcznie
napisany adapter bez grafu modułów nie spełniają kontraktu M11.

## Poza zakresem pierwszej wersji

- zdalny publiczny rejestr pakietów,
- automatyczny ORM i generowanie dowolnych zapytań z nazw metod,
- rozproszone transakcje obejmujące wiele baz,
- replikacja i klaster SQLite,
- bezpośredni dostęp `@embedded` lub `@report` do pliku serwera,
- automatyczny binding i generowany adapter domenowy dla PostgreSQL,
- sekrety zapisane w źródle `.jnx`.
