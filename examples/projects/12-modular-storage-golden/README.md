# Moduły, wymienne storage, restart i panel

> **Bieżący bloker — 2026-09-24:** `check` kończy się kodem 1 z P0001 przy `import storage::…` (`src/main.jnx`, `src/application/home_service.jnx`) i polu `key` (`src/domain/observations.jnx`). Opisy oraz raporty E2E poniżej dokumentują wcześniejsze próby, nie aktualny sukces. Klasyfikacja i diagnostyki nie są częścią tego repozytorium. Naprawa parsera lub zgodności przykładu pozostaje osobnym zadaniem.

Ten projekt jest wykonywalnym dowodem Golden Modular Storage E2E. Nie zaczyna
od awarii. Najpierw buduje mały, pełny system domu, zapisuje prawdziwą
obserwację, a dopiero potem świadomie restartuje proces, aby sprawdzić trwałość.

## Co składa się na system

```text
domain::observations
        │
        ├── storage::home_store ── extern storage ── storage.Store
        │                                      │
        │                         Storage Runtime ABI 0.1
        │                                      │
        │                         osobny pakiet providera
        │
        ├── application::home_service ── REST API
        │
        └── dashboard::home_panel
                    ▲
                    │ chan<HomeSnapshot>
          PublishHomeSnapshot @server
```

Pięć plików `.jnx` ma rozdzielone odpowiedzialności. Panel nie otwiera pliku
SQLite. Otrzymuje `HomeSnapshot` z zadania `PublishHomeSnapshot @server`, a
człowiek lub inna aplikacja korzysta z tej samej granicy przez REST.

Adapter `user/server/home_store.go` jest jawny, ponieważ Junexis nie zgaduje
zapytań z nazw metod. Kod domenowy widzi `HomeStore`, natomiast adapter korzysta
ze wspólnego `junexis.storage/0.1`. W M12.7 wygenerowany serwer nie zawiera
sterownika bazy. Uruchamia neutralnego klienta `junexis.storage.runtime/0.1`,
a deployment wskazuje host, pakiet providera, jego ID i konfigurację. Zmiana
memory → SQLite → PostgreSQL nie zmienia plików `.jnx`, adaptera ani binarnego
serwera Go.

## Rytm próby

1. `junexis check .` sprawdza graf importów i semantykę pięciu modułów.
2. `junexis build .` generuje serwer, raport, manifesty, migrację i blokadę
   modułów.
3. Serwer zapisuje obserwację przez `POST /api/home/observations`.
4. Transakcja kontrolna jest celowo wycofywana i nie zostawia rekordu.
5. Proces serwera zostaje zakończony i uruchomiony ponownie na tej samej bazie.
6. `GET /api/home/living-room` odczytuje zatwierdzony rekord przez zadanie
   serwera; odpowiedź potwierdza `restored_after_restart=true`.
7. Dowód zapisuje provider, moduły, migracje, Build ID, Bundle ID i granicę
   panelu.

## Rytuał M12.8 przez `junexis`

Repozytorium nie przechowuje binarnych paczek providerów. Po ich zbudowaniu
skrypt przygotowuje izolowany projekt dowodowy z bindingiem SQLite:

```powershell
$stage = ./verification/prepare-m12-runtime-project.ps1 `
  -ProviderPackage ../../../junexis-storage-providers/Junexis.Storage.Provider.Sqlite/bin/Release/net10.0/provider-package

junexis run $stage --duration 8 --no-open `
  --storage-runtime-host ../../../junexis-runtime/Junexis.Storage.Runtime.Host/bin/Release/net10.0/Junexis.Storage.Runtime.Host.dll

junexis package $stage `
  --storage-runtime-host ../../../junexis-runtime/Junexis.Storage.Runtime.Host/bin/Release/net10.0/Junexis.Storage.Runtime.Host.dll
```

Pierwsze polecenie weryfikuje lockfile bezpośrednio przed startem. Drugie
umieszcza ten sam host i provider wewnątrz System Bundle.

Po zbudowaniu natywnego serwera próbę uruchamia:

```powershell
./verification/verify-golden-storage.ps1 `
  -Executable ./out/server/modular-storage-golden.exe `
  -StorageRuntimeHost ../../../junexis-runtime/Junexis.Storage.Runtime.Host/bin/Release/net10.0/Junexis.Storage.Runtime.Host.dll `
  -ProviderEntrypoint ../../../junexis-storage-providers/Junexis.Storage.Provider.Sqlite/bin/Release/net10.0/Junexis.Storage.Provider.Sqlite.dll
```

Skrypt tworzy bazę wyłącznie w należącym do próby katalogu tymczasowym i usuwa
ją po zakończeniu. Nie dotyka danych użytkownika.

Po utworzeniu System Bundle próbę można wykonać bez zewnętrznego hosta i bez
checkoutu źródeł:

```powershell
./verification/verify-system-bundle.ps1 `
  -Bundle ./out/system/modular-storage-golden-<build>.system-bundle.zip
```

Skrypt uruchamia z rozpakowanej paczki natywny serwer oraz statyczny runtime
panelu, zapisuje obserwację, restartuje serwer i potwierdza odtworzenie danych
wraz z rzeczywistymi Build ID i Bundle ID. M12.8 umieszcza zweryfikowany host,
pakiet providera i bezpieczny manifest deploymentu bezpośrednio w paczce.

Próbę z istniejącym testowym serwerem PostgreSQL uruchamia:

```powershell
./verification/verify-postgresql-storage.ps1 `
  -Executable ./out/server/runtime/junexis-server.exe `
  -ConnectionString $env:JUNEXIS_POSTGRESQL_TEST_URL `
  -StorageRuntimeHost <ścieżka-host> `
  -ProviderEntrypoint <ścieżka-postgresql-provider>
```

Skrypt wymaga jawnego adresu testowej bazy, używa unikalnego identyfikatora
rekordu, kończy pierwszy proces i sprawdza odczyt w drugim. Nie tworzy ani nie
usuwa samego serwera PostgreSQL.

## Czego ten dowód nie obiecuje

To nie jest automatyczny ORM. Adapter
domenowy pozostaje kodem autora i świadomie mapuje struktury na rekordy.
Provider pamięciowy jest wymienny semantycznie, ale nie przechodzi próby
trwałości po restarcie — jego ulotność jest częścią kontraktu.
Provider PostgreSQL ma skompilowany kontrakt i mapowanie SQLSTATE. Status
pełnego E2E otrzyma dopiero po wykonaniu osobnej próby na rzeczywistym serwerze.

## Laboratorium M12.9

Ten sam projekt prowadzi teraz przez trzy zakresy kontroli: trwały zapis w
SQLite, wymianę SQLite na memory bez zmiany kodu `.jnx` oraz ekspercką kontrolę
lockfile, sekretów i System Bundle. Rozpocznij od
[`laboratory/README.md`](laboratory/README.md).
